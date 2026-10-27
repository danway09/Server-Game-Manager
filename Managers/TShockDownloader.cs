using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // TShock не публикует готовый server.jar как Paper — сборки лежат на
    // GitHub Releases отдельным zip-архивом на каждую ОС. Качаем именно
    // Windows-сборку и распаковываем прямо в папку сервера.
    public class TShockDownloader
    {

        private readonly HttpClient client = new HttpClient();



        public TShockDownloader()
        {

            // GitHub API требует указать User-Agent, иначе отдаёт 403
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("MCServerManager", "1.0")
            );

        }








        // Список нескольких последних релизов — для выпадающего списка версий
        public async Task<List<string>> GetVersions()
        {

            const string url =
                "https://api.github.com/repos/Pryaxis/TShock/releases";



            try
            {

                string json =
                    await client.GetStringAsync(url);



                using JsonDocument doc =
                    JsonDocument.Parse(json);



                List<string> list = new();



                foreach (JsonElement release in doc.RootElement.EnumerateArray())
                {

                    string? tag =
                        release.GetProperty("tag_name").GetString();



                    if (tag != null)
                    {
                        list.Add(tag);
                    }

                }



                return list;

            }
            catch (Exception ex)
            {

                throw new Exception(
                    $"Не удалось получить список версий TShock по адресу {url}: {ex.Message}"
                );

            }

        }








        // version = null или пусто → берём самый последний релиз
        public async Task InstallTShock(
            string serverPath,
            string? version,
            Action<string>? onOutput = null)
        {

            try
            {

                string releaseUrl =
                    string.IsNullOrWhiteSpace(version)
                        ? "https://api.github.com/repos/Pryaxis/TShock/releases/latest"
                        : $"https://api.github.com/repos/Pryaxis/TShock/releases/tags/{version}";



                onOutput?.Invoke("Ищу подходящий релиз TShock...");



                string json =
                    await client.GetStringAsync(releaseUrl);



                using JsonDocument doc =
                    JsonDocument.Parse(json);



                string tagName =
                    doc.RootElement.GetProperty("tag_name").GetString() ?? "unknown";



                // Ищем среди файлов релиза Windows-сборку (обычно содержит
                // "win" в названии). Если не находим — берём первый попавшийся
                // zip, лучше так, чем вообще ничего не установить.
                string? assetUrl = null;

                string? assetName = null;



                foreach (JsonElement asset in doc.RootElement.GetProperty("assets").EnumerateArray())
                {

                    string name =
                        asset.GetProperty("name").GetString() ?? "";



                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;



                    if (name.Contains("win", StringComparison.OrdinalIgnoreCase))
                    {

                        assetUrl = asset.GetProperty("browser_download_url").GetString();

                        assetName = name;


                        break;

                    }



                    // запасной вариант, если ни один явно не Windows
                    if (assetUrl == null)
                    {

                        assetUrl = asset.GetProperty("browser_download_url").GetString();

                        assetName = name;

                    }

                }



                if (assetUrl == null)
                {

                    throw new Exception(
                        "В релизе " + tagName + " не нашлось ни одного zip-файла для скачивания."
                    );

                }



                onOutput?.Invoke($"Скачиваю {assetName}...");



                string zipPath =
                    Path.Combine(serverPath, "tshock_download.zip");



                using (HttpResponseMessage response = await client.GetAsync(assetUrl))
                {

                    response.EnsureSuccessStatusCode();



                    await using FileStream fs =
                        new FileStream(zipPath, FileMode.Create);



                    await response.Content.CopyToAsync(fs);

                }



                onOutput?.Invoke("Распаковываю...");



                string extractTemp =
                    Path.Combine(serverPath, "_tshock_extract_temp");



                if (Directory.Exists(extractTemp))
                {
                    Directory.Delete(extractTemp, true);
                }



                ZipFile.ExtractToDirectory(zipPath, extractTemp);



                // GitHub-архивы часто содержат один вложенный подкаталог —
                // если так, поднимаем его содержимое на уровень выше
                string sourceDir = extractTemp;



                string[] topEntries =
                    Directory.GetFileSystemEntries(extractTemp);



                if (topEntries.Length == 1 && Directory.Exists(topEntries[0]))
                {
                    sourceDir = topEntries[0];
                }



                foreach (string file in Directory.GetFiles(sourceDir))
                {

                    string dest =
                        Path.Combine(serverPath, Path.GetFileName(file));



                    File.Copy(file, dest, true);

                }



                foreach (string dir in Directory.GetDirectories(sourceDir))
                {

                    string destDir =
                        Path.Combine(serverPath, Path.GetFileName(dir));



                    CopyDirectory(dir, destDir);

                }



                Directory.Delete(extractTemp, true);



                FileHelper.TryDeleteWithRetries(zipPath);



                // Ищем реальный exe-файл для запуска
                string? exePath =
                    Directory.GetFiles(serverPath, "*.exe", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f =>
                            Path.GetFileName(f).Contains("TShock", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).Contains("Server", StringComparison.OrdinalIgnoreCase)
                        );



                if (exePath == null)
                {

                    throw new Exception(
                        "Архив распакован, но exe-файл сервера TShock не найден " +
                        "в папке. Возможно, изменилась структура релиза — " +
                        "проверь папку сервера вручную."
                    );

                }



                onOutput?.Invoke("Готово! Найден: " + Path.GetFileName(exePath));

            }
            catch (Exception ex)
            {

                throw new Exception(
                    "Ошибка установки TShock: " + ex.Message
                );

            }

        }








        private void CopyDirectory(string sourceDir, string destDir)
        {

            Directory.CreateDirectory(destDir);



            foreach (string file in Directory.GetFiles(sourceDir))
            {

                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);

            }



            foreach (string dir in Directory.GetDirectories(sourceDir))
            {

                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));

            }

        }

    }
}
