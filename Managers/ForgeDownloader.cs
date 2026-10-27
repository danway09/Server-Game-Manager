using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    public class ForgeDownloader
    {
        private const string PromotionsUrl =
            "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";

        // Для рекомендованной (или последней) сборки Forge для заданной
        // версии Minecraft берём список promotions_slim.json
        public async Task InstallForge(
            string serverPath,
            string minecraftVersion,
            Action<string>? onOutput = null)
        {
            try
            {
                string forgeVersion = await FindForgeBuild(minecraftVersion);

                string fileName =
                    $"forge-{minecraftVersion}-{forgeVersion}-installer.jar";

                string url =
                    "https://maven.minecraftforge.net/net/minecraftforge/forge/" +
                    $"{minecraftVersion}-{forgeVersion}/{fileName}";

                string installerPath =
                    Path.Combine(serverPath, "forge-installer.jar");

                onOutput?.Invoke("Скачиваю установщик Forge...");

                await DownloadHelper.DownloadFileAsync(url, installerPath);

                onOutput?.Invoke(
                    "Запускаю установщик Forge — скачивание библиотек может занять несколько минут...");

                string installerOutput = await RunInstaller(serverPath, installerPath, onOutput);

                FileHelper.TryDeleteWithRetries(installerPath);

                // Успех проверяем по факту: старые сборки Forge не создают
                // run.bat (только forge-*.jar + libraries), поэтому одной
                // проверки run.bat мало (см. ForgeInstallStatus).
                if (!ForgeInstallStatus.IsInstalled(serverPath))
                {
                    throw new Exception(
                        "Установщик завершился без ошибок, но в папке сервера не " +
                        "появилось ни run.bat, ни серверного forge-*.jar — " +
                        "установка не удалась по неизвестной причине.\n\n" +
                        "Вывод установщика:\n" + Tail(installerOutput, 1500) + "\n\n" +
                        "Файлы в папке сервера:\n" + ForgeInstallStatus.DescribeDirectory(serverPath)
                    );
                }
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "Ошибка установки Forge: " + ex.Message +
                    "\n\nВозможно, для этой версии Minecraft нет сборки Forge, " +
                    "либо изменился формат данных на сайте Forge."
                );
            }
        }

        private async Task<string> FindForgeBuild(string minecraftVersion)
        {
            string json = await DownloadHelper.GetStringAsync(PromotionsUrl);

            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement promos = doc.RootElement.GetProperty("promos");

            // сперва пробуем "recommended", если нет — берём "latest"
            string recommendedKey = $"{minecraftVersion}-recommended";
            string latestKey = $"{minecraftVersion}-latest";

            if (promos.TryGetProperty(recommendedKey, out JsonElement recommended))
            {
                return recommended.GetString()!;
            }

            if (promos.TryGetProperty(latestKey, out JsonElement latest))
            {
                return latest.GetString()!;
            }

            throw new Exception(
                $"Для версии {minecraftVersion} нет доступных сборок Forge"
            );
        }

        // Список версий Minecraft, для которых есть сборки Forge.
        // Кэшируется, чтобы переключение ядра в UI не запрашивало сеть каждый раз.
        public async Task<List<string>> GetSupportedMinecraftVersions(bool forceRefresh = false)
        {
            return await VersionListCache.GetAsync(
                "forge-versions",
                async () =>
                {
                    string json = await DownloadHelper.GetStringAsync(PromotionsUrl);

                    using JsonDocument doc = JsonDocument.Parse(json);

                    HashSet<string> versions = new();

                    foreach (JsonProperty prop in doc.RootElement.GetProperty("promos").EnumerateObject())
                    {
                        int dash = prop.Name.LastIndexOf('-');

                        if (dash > 0)
                        {
                            versions.Add(prop.Name.Substring(0, dash));
                        }
                    }

                    return versions
                        .OrderByDescending(v => v, Comparer<string>.Create(CompareVersions))
                        .ToList();
                },
                forceRefresh);
        }

        // Простое сравнение версий вида "1.21.11" — по числовым частям
        private static int CompareVersions(string a, string b)
        {
            int[] partsA = a.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
            int[] partsB = b.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();

            int length = Math.Max(partsA.Length, partsB.Length);

            for (int i = 0; i < length; i++)
            {
                int ai = i < partsA.Length ? partsA[i] : 0;
                int bi = i < partsB.Length ? partsB[i] : 0;

                int cmp = ai.CompareTo(bi);

                if (cmp != 0)
                    return cmp;
            }

            return 0;
        }

        // Запускает установщик Forge и стримит его вывод (onOutput) в окно
        // прогресса; возвращает полный вывод для диагностики.
        private async Task<string> RunInstaller(
            string serverPath,
            string installerPath,
            Action<string>? onOutput)
        {
            var info = new ProcessStartInfo();

            info.FileName = "java";

            // WorkingDirectory уже указывает на папку сервера, поэтому тут
            // нужно только ИМЯ файла, а не полный путь — иначе он задваивается
            // (java ищет serverPath+serverPath+файл)
            info.Arguments = $"-jar \"{Path.GetFileName(installerPath)}\" --installServer";

            info.WorkingDirectory = serverPath;

            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            var process = Process.Start(info);

            StringBuilder fullOutput = new StringBuilder();

            object gate = new object();

            void Collect(string? line)
            {
                if (line == null)
                {
                    return;
                }

                lock (gate)
                {
                    fullOutput.AppendLine(line);
                }

                onOutput?.Invoke(line);
            }

            process!.OutputDataReceived += (s, e) => Collect(e.Data);
            process.ErrorDataReceived += (s, e) => Collect(e.Data);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            string output;

            lock (gate)
            {
                output = fullOutput.ToString();
            }

            if (process.ExitCode != 0)
            {
                throw new Exception(
                    "Установщик Forge завершился с ошибкой (код " +
                    process.ExitCode + ").\n\nВывод установщика:\n" + Tail(output, 1500)
                );
            }

            return output;
        }

        private static string Tail(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(пусто)";
            }

            return text.Length <= maxLength
                ? text
                : "..." + text.Substring(text.Length - maxLength);
        }
    }
}
