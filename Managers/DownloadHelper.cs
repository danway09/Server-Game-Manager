using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // Единая точка сетевых загрузок приложения:
    //  - один общий HttpClient (соединения переиспользуются, HTTP/2);
    //  - подключения не зависают на хостах с "мёртвым" IPv6 (параллельный подбор адресов);
    //  - большие файлы качаются в несколько Range-соединений, если сервер умеет,
    //    с безопасным откатом на обычную загрузку при любой аномалии;
    //  - прогресс не чаще ~10 раз в секунду (раньше событие дёргалось на
    //    каждые 8 КБ — это тысячи вызовов на файл и захлёбнутый UI-поток).
    public static class DownloadHelper
    {
        // С этого размера пробуем многопоточную загрузку.
        private const int SegmentedMinSize = 8 * 1024 * 1024;

        private const int SegmentCount = 4;

        private const int CopyBufferSize = 256 * 1024;

        private const int IdleTimeoutMs = 60_000;

        // Диагностические сообщения (какой путь загрузки выбран и почему) —
        // полезно для отладки; в приложении подписчиков нет.
        public static event Action<string>? Log;

        public static readonly HttpClient SharedClient = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                MaxConnectionsPerServer = 16,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(30),

                // На некоторых сетях IPv6-подключения "висят" (SYN пропадает),
                // и стандартный стек сидит до ConnectTimeout, а потом падает
                // (так было с хостами Forge на этой машине). Подбираем адрес
                // сами: все адреса хоста пробуются параллельно, первый успех
                // выигрывает — как у браузеров и curl.
                ConnectCallback = ConnectRacingAsync
            };

            var client = new HttpClient(handler)
            {
                // Дефолтный таймаут HttpClient (100 секунд) обрывал бы большие
                // загрузки на медленной сети — ограничиваем только простои
                // (см. IdleTimeoutMs ниже).
                Timeout = Timeout.InfiniteTimeSpan
            };

            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            return client;
        }

        public static async Task<string> GetStringAsync(string url, TimeSpan? timeout = null)
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));

            return await SharedClient.GetStringAsync(url, cts.Token);
        }

        // Скачивает файл: временный файл + атомарная замена, безопасный откат
        // многопоточности, троттлинг прогресса.
        public static async Task DownloadFileAsync(
            string url,
            string savePath,
            Action<int>? onProgress = null,
            CancellationToken ct = default)
        {
            string tempPath = savePath + ".part";

            try
            {
                await DownloadAttemptAsync(url, tempPath, onProgress, ct);

                await ReplaceFileAsync(tempPath, savePath);

                onProgress?.Invoke(100);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        private static async Task DownloadAttemptAsync(
            string url,
            string tempPath,
            Action<int>? onProgress,
            CancellationToken ct)
        {
            var (rangeSupported, size) = await ProbeAsync(url, ct);

            if (rangeSupported && size.HasValue && size.Value >= SegmentedMinSize)
            {
                Log?.Invoke($"Многопоточная загрузка: {SegmentCount} соединения, {size.Value} байт");

                try
                {
                    await DownloadSegmentedAsync(url, tempPath, size.Value, onProgress, ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Не рискуем: любой сбой сегментов — стираем огрызок
                    // и качаем обычным способом с нуля.
                    Log?.Invoke("Многопоточная загрузка не удалась, откат на однопоточную: " + ex.Message);
                    TryDelete(tempPath);
                }
            }
            else
            {
                Log?.Invoke(rangeSupported
                    ? "Однопоточная загрузка (файл меньше порога многопоточности)"
                    : "Однопоточная загрузка (сервер не поддерживает Range)");
            }

            await DownloadSingleAsync(url, tempPath, size, onProgress, ct);
        }

        // Проверяем, поддерживает ли сервер догрузку кусками, и узнаём размер.
        private static async Task<(bool RangeSupported, long? Size)> ProbeAsync(string url, CancellationToken ct)
        {
            long? size = null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Range = new RangeHeaderValue(0, 0);

            using HttpResponseMessage response =
                await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                size = response.Content.Headers.ContentRange?.Length;

                return (true, size);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Не удалось скачать файл (код {(int)response.StatusCode}). Ссылка: {url}");
            }

            size = response.Content.Headers.ContentLength;

            return (false, size);
        }

        private static async Task DownloadSegmentedAsync(
            string url,
            string tempPath,
            long totalSize,
            Action<int>? onProgress,
            CancellationToken ct)
        {
            using var file = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                CopyBufferSize, FileOptions.Asynchronous);

            file.SetLength(totalSize);

            long done = 0;

            Action reportProgress = CreateProgressReporter(
                onProgress, totalSize, () => Interlocked.Read(ref done));

            long segmentSize = (totalSize + SegmentCount - 1) / SegmentCount;

            var tasks = new Task[SegmentCount];

            for (int i = 0; i < SegmentCount; i++)
            {
                long start = (long)i * segmentSize;

                if (start >= totalSize)
                {
                    tasks[i] = Task.CompletedTask;
                    continue;
                }

                long end = Math.Min(start + segmentSize, totalSize) - 1;

                tasks[i] = DownloadSegmentAsync(
                    url, file, start, end,
                    bytes => { Interlocked.Add(ref done, bytes); reportProgress(); },
                    ct);
            }

            await Task.WhenAll(tasks);

            await file.FlushAsync(ct);
        }

        private static async Task DownloadSegmentAsync(
            string url,
            FileStream file,
            long start,
            long end,
            Action<long> addBytes,
            CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Range = new RangeHeaderValue(start, end);

            using HttpResponseMessage response =
                await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new Exception(
                    $"Сервер вернул {(int)response.StatusCode} вместо части файла (206).");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);

            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            byte[] buffer = new byte[CopyBufferSize];

            long offset = start;

            while (true)
            {
                idleCts.CancelAfter(IdleTimeoutMs);

                int read;

                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), idleCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new Exception(
                        $"Загрузка прервана: данные не приходят дольше {IdleTimeoutMs / 1000} секунд.");
                }

                if (read == 0)
                {
                    break;
                }

                await RandomAccess.WriteAsync(file.SafeFileHandle, buffer.AsMemory(0, read), offset, ct);

                offset += read;

                addBytes(read);
            }

            if (offset != end + 1)
            {
                throw new Exception(
                    $"Сегмент скачан не полностью ({offset - start} из {end + 1 - start} байт).");
            }
        }

        private static async Task DownloadSingleAsync(
            string url,
            string tempPath,
            long? knownSize,
            Action<int>? onProgress,
            CancellationToken ct)
        {
            using HttpResponseMessage response =
                await SharedClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Не удалось скачать файл (код {(int)response.StatusCode}). Ссылка: {url}");
            }

            long? size = response.Content.Headers.ContentLength ?? knownSize;

            await using Stream source = await response.Content.ReadAsStreamAsync(ct);

            await using var target = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

            long done = 0;

            Action reportProgress = size.HasValue
                ? CreateProgressReporter(onProgress, size.Value, () => Interlocked.Read(ref done))
                : () => { };

            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            byte[] buffer = new byte[CopyBufferSize];

            while (true)
            {
                idleCts.CancelAfter(IdleTimeoutMs);

                int read;

                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), idleCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new Exception(
                        $"Загрузка прервана: данные не приходят дольше {IdleTimeoutMs / 1000} секунд.");
                }

                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), ct);

                done += read;

                reportProgress();
            }

            await target.FlushAsync(ct);
        }

        // Прогресс не чаще ~10 раз в секунду и только при смене процента.
        private static Action CreateProgressReporter(
            Action<int>? onProgress,
            long totalSize,
            Func<long> getDone)
        {
            if (onProgress == null || totalSize <= 0)
            {
                return static () => { };
            }

            object gate = new object();
            int lastPercent = -1;
            DateTime lastReport = DateTime.MinValue;

            return () =>
            {
                long done = getDone();

                int percent = (int)Math.Min(100, done * 100 / totalSize);

                lock (gate)
                {
                    if (percent == lastPercent)
                    {
                        return;
                    }

                    DateTime now = DateTime.UtcNow;

                    if (percent < 100 && (now - lastReport).TotalMilliseconds < 100)
                    {
                        return;
                    }

                    lastPercent = percent;
                    lastReport = now;
                }

                onProgress(percent);
            };
        }

        private static async Task ReplaceFileAsync(string tempPath, string savePath)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(savePath))
                    {
                        File.Delete(savePath);
                    }

                    File.Move(tempPath, savePath);

                    return;
                }
                catch when (attempt < 4)
                {
                    await Task.Delay(300);
                }
            }
        }

        // Happy Eyeballs-подобное подключение: все адреса хоста пробуются
        // параллельно, побеждает первый успешно подключившийся; остальные
        // попытки отменяются, их сокеты закрываются.
        private static async ValueTask<Stream> ConnectRacingAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            IPAddress[] addresses =
                await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

            if (addresses.Length == 0)
            {
                throw new HttpRequestException(
                    $"Не удалось разрешить адрес {context.DnsEndPoint.Host}");
            }

            if (addresses.Length > 6)
            {
                addresses = addresses.Take(6).ToArray();
            }

            using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var attempts = new List<Task<Socket>>();

            try
            {
                foreach (IPAddress address in addresses)
                {
                    attempts.Add(ConnectOneAsync(address, context.DnsEndPoint.Port, raceCts.Token));
                }

                Exception? lastError = null;

                while (attempts.Count > 0)
                {
                    Task<Socket> finished = await Task.WhenAny(attempts).ConfigureAwait(false);

                    attempts.Remove(finished);

                    if (finished.IsCompletedSuccessfully)
                    {
                        // Победитель найден — остальные попытки не нужны.
                        raceCts.Cancel();

                        Socket winner = await finished;

                        return new NetworkStream(winner, ownsSocket: true);
                    }

                    try
                    {
                        await finished;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }

                throw new HttpRequestException(
                    $"Не удалось подключиться к {context.DnsEndPoint.Host}:{context.DnsEndPoint.Port}",
                    lastError);
            }
            finally
            {
                // Отменённые/проигравшие попытки: как только завершатся —
                // закрываем их сокеты (неуспешные уже закрываются в ConnectOneAsync).
                foreach (Task<Socket> attempt in attempts)
                {
                    _ = attempt.ContinueWith(
                        t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion)
                            {
                                t.Result.Dispose();
                            }
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }

        private static async Task<Socket> ConnectOneAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken)
                    .ConfigureAwait(false);

                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // мусорный .part не критичен
            }
        }
    }
}
