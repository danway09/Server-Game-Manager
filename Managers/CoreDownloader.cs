using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    public class CoreDownloader
    {
        public event Action<int>? DownloadProgress;

        // установка Paper
        public async Task DownloadPaper(
            string serverPath,
            string version)
        {
            string buildUrl =
                $"https://fill.papermc.io/v3/projects/paper/versions/{version}/builds/latest";

            try
            {
                string json =
                    await DownloadHelper.GetStringAsync(buildUrl);

                using JsonDocument doc = JsonDocument.Parse(json);

                JsonElement downloads =
                    doc.RootElement.GetProperty("downloads");

                string? downloadUrl = null;

                // Обычно серверная сборка лежит под ключом "server:default" —
                // но на случай, если Paper назовёт ключ иначе, подстраховываемся
                // и берём первый доступный вариант
                if (downloads.TryGetProperty("server:default", out JsonElement serverDl))
                {
                    downloadUrl = serverDl.GetProperty("url").GetString();
                }
                else
                {
                    foreach (JsonProperty prop in downloads.EnumerateObject())
                    {
                        downloadUrl = prop.Value.GetProperty("url").GetString();

                        break;
                    }
                }

                if (downloadUrl == null)
                {
                    throw new Exception(
                        "В ответе Fill API не нашлось ни одной ссылки на скачивание"
                    );
                }

                await DownloadFile(downloadUrl, serverPath);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Ошибка установки Paper (адрес {buildUrl}): {ex.Message}"
                );
            }
        }

        // установка Purpur
        public async Task DownloadPurpur(
            string serverPath,
            string version)
        {
            try
            {
                string url =
                    $"https://api.purpurmc.org/v2/purpur/{version}/latest/download";

                await DownloadFile(url, serverPath);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "Ошибка установки Purpur: " + ex.Message
                );
            }
        }

        // Список версий, под которые есть сборки Paper. Кэшируется.
        public async Task<List<string>> GetPaperVersions(bool forceRefresh = false)
        {
            const string url = "https://fill.papermc.io/v3/projects/paper";

            try
            {
                return await VersionListCache.GetAsync(
                    "paper-versions",
                    async () =>
                    {
                        string json =
                            await DownloadHelper.GetStringAsync(url);

                        using JsonDocument doc = JsonDocument.Parse(json);

                        List<string> list = new();

                        foreach (JsonProperty family in doc.RootElement.GetProperty("versions").EnumerateObject())
                        {
                            foreach (JsonElement v in family.Value.EnumerateArray())
                            {
                                list.Add(v.GetString()!);
                            }
                        }

                        return list;
                    },
                    forceRefresh);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Не удалось получить версии Paper по адресу {url}: {ex.Message}"
                );
            }
        }

        // Список версий, под которые есть сборки Purpur. Кэшируется.
        public async Task<List<string>> GetPurpurVersions(bool forceRefresh = false)
        {
            const string url = "https://api.purpurmc.org/v2/purpur";

            try
            {
                return await VersionListCache.GetAsync(
                    "purpur-versions",
                    async () =>
                    {
                        string json =
                            await DownloadHelper.GetStringAsync(url);

                        using JsonDocument doc = JsonDocument.Parse(json);

                        List<string> list = new();

                        foreach (JsonElement v in doc.RootElement.GetProperty("versions").EnumerateArray())
                        {
                            list.Add(v.GetString()!);
                        }

                        list.Reverse();

                        return list;
                    },
                    forceRefresh);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Не удалось получить версии Purpur по адресу {url}: {ex.Message}"
                );
            }
        }

        // общая загрузка
        private async Task DownloadFile(
            string url,
            string serverPath)
        {
            string save =
                Path.Combine(
                    serverPath,
                    "server.jar"
                );

            await DownloadHelper.DownloadFileAsync(
                url,
                save,
                progress => DownloadProgress?.Invoke(progress));
        }

        // проверка ядра
        public bool HasCore(string path)
        {
            return File.Exists(
                Path.Combine(
                    path,
                    "server.jar"
                )
            );
        }
    }
}
