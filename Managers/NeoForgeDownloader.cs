using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    public class NeoForgeDownloader
    {

        // сетевые операции идут через общий DownloadHelper.SharedClient



        private const string VersionsUrl =
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";








        public async Task InstallNeoForge(string serverPath, string minecraftVersion)
        {

            try
            {

                string neoforgeVersion =
                    await FindNeoForgeVersion(minecraftVersion);



                string fileName =
                    $"neoforge-{neoforgeVersion}-installer.jar";



                string url =
                    "https://maven.neoforged.net/releases/net/neoforged/neoforge/" +
                    $"{neoforgeVersion}/{fileName}";



                string installerPath =
                    Path.Combine(serverPath, "neoforge-installer.jar");



                await DownloadHelper.DownloadFileAsync(url, installerPath);



                await RunInstaller(serverPath, installerPath);



                FileHelper.TryDeleteWithRetries(installerPath);



                if (!File.Exists(Path.Combine(serverPath, "run.bat")))
                {

                    throw new Exception(
                        "Инсталлятор завершился без ошибок, но run.bat не " +
                        "появился — установка не удалась по неизвестной причине."
                    );

                }

            }
            catch (Exception ex)
            {

                throw new Exception(
                    "Ошибка установки NeoForge: " + ex.Message +
                    "\n\nNeoForge поддерживает версии начиная примерно с 1.20.1 — " +
                    "для более старых версий Minecraft используй обычный Forge."
                );

            }

        }








        // NeoForge нумерует версии как <MAJOR>.<MINOR>.<PATCH>, где
        // MAJOR.MINOR соответствует версии Minecraft без ведущей "1."
        // (например, Minecraft 1.21.1 -> версии NeoForge вида 21.1.x)
        // Список версий Minecraft, для которых есть сборки NeoForge
        public async Task<List<string>> GetSupportedMinecraftVersions()
        {

            string json =
                await DownloadHelper.GetStringAsync(VersionsUrl);



            using JsonDocument doc =
                JsonDocument.Parse(json);



            HashSet<string> set = new();



            foreach (JsonElement v in doc.RootElement.GetProperty("versions").EnumerateArray())
            {

                string version = v.GetString()!;



                string[] parts = version.Split('.');



                if (parts.Length >= 2)
                {

                    set.Add("1." + parts[0] + "." + parts[1]);

                }

            }



            return set
                .OrderByDescending(v => v, Comparer<string>.Create(CompareMcVersions))
                .ToList();

        }








        private static int CompareMcVersions(string a, string b)
        {

            int[] partsA =
                a.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();



            int[] partsB =
                b.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();



            int length =
                Math.Max(partsA.Length, partsB.Length);



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








        private async Task<string> FindNeoForgeVersion(string minecraftVersion)
        {

            string prefix =
                minecraftVersion.StartsWith("1.")
                    ? minecraftVersion.Substring(2)
                    : minecraftVersion;



            // добавляем точку, чтобы "21.1" не совпало с "21.10"
            string matchPrefix = prefix + ".";



            string json =
                await DownloadHelper.GetStringAsync(VersionsUrl);



            using JsonDocument doc =
                JsonDocument.Parse(json);



            var matching =
                doc.RootElement.GetProperty("versions")
                    .EnumerateArray()
                    .Select(v => v.GetString()!)
                    .Where(v => v.StartsWith(matchPrefix))
                    .ToList();



            if (matching.Count == 0)
            {

                throw new Exception(
                    $"Не найдено сборок NeoForge для версии Minecraft {minecraftVersion}"
                );

            }



            // сортируем численно по частям версии, берём самую свежую
            string best =
                matching
                    .OrderBy(v => v
                        .Split('.')
                        .Select(part => int.TryParse(part, out int n) ? n : 0)
                        .ToList(),
                        new VersionPartsComparer())
                    .Last();



            return best;

        }








        private class VersionPartsComparer : System.Collections.Generic.IComparer<System.Collections.Generic.List<int>>
        {

            public int Compare(System.Collections.Generic.List<int>? a, System.Collections.Generic.List<int>? b)
            {

                int length = Math.Max(a!.Count, b!.Count);



                for (int i = 0; i < length; i++)
                {

                    int ai = i < a.Count ? a[i] : 0;

                    int bi = i < b.Count ? b[i] : 0;



                    int cmp = ai.CompareTo(bi);



                    if (cmp != 0)
                        return cmp;

                }



                return 0;

            }

        }








        private async Task RunInstaller(string serverPath, string installerPath)
        {

            var info =
                new System.Diagnostics.ProcessStartInfo();



            info.FileName = "java";



            // ГЛАВНЫЙ ФИКС: WorkingDirectory уже указывает на папку сервера,
            // поэтому тут нужно только ИМЯ файла, а не полный относительный
            // путь — иначе он задваивается (java ищет serverPath+serverPath+файл)
            info.Arguments =
                $"-jar \"{Path.GetFileName(installerPath)}\" --installServer";



            info.WorkingDirectory = serverPath;



            info.UseShellExecute = false;



            info.CreateNoWindow = true;



            info.RedirectStandardOutput = true;

            info.RedirectStandardError = true;



            var process =
                System.Diagnostics.Process.Start(info);



            // ГЛАВНЫЙ ФИКС: раньше вывод инсталлятора нигде не читался —
            // мы видели только код возврата, а настоящую причину сбоя
            // (например, "no такой версии" или ошибку сети) инсталлятор
            // печатает именно в stdout/stderr. Читаем асинхронно (иначе
            // можно словить дедлок, если буфер вывода переполнится, пока
            // мы синхронно ждём WaitForExit).
            Task<string> outputTask =
                process!.StandardOutput.ReadToEndAsync();



            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();



            await process.WaitForExitAsync();



            string output = await outputTask;

            string error = await errorTask;



            if (process.ExitCode != 0)
            {

                string details =
                    string.IsNullOrWhiteSpace(error)
                        ? output
                        : error;



                // берём хвост вывода — там обычно и есть суть ошибки
                if (details.Length > 1500)
                {
                    details = "..." + details.Substring(details.Length - 1500);
                }



                throw new Exception(
                    "Инсталлятор NeoForge завершился с ошибкой (код " +
                    process.ExitCode + ").\n\nВывод инсталлятора:\n" + details
                );

            }

        }

    }
}