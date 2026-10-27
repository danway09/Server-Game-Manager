using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    public class VanillaDownloader
    {
        public event Action<int>? DownloadProgress;

        private const string ManifestUrl =
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        public async Task DownloadVanilla(string serverPath, string version)
        {
            try
            {
                // 1. общий манифест версий — находим ссылку на JSON конкретной версии
                string manifestJson = await DownloadHelper.GetStringAsync(ManifestUrl);

                using JsonDocument manifestDoc = JsonDocument.Parse(manifestJson);

                string? versionUrl = null;

                foreach (JsonElement v in manifestDoc.RootElement.GetProperty("versions").EnumerateArray())
                {
                    if (v.GetProperty("id").GetString() == version)
                    {
                        versionUrl = v.GetProperty("url").GetString();

                        break;
                    }
                }

                if (versionUrl == null)
                {
                    throw new Exception(
                        $"Версия {version} не найдена в манифесте Mojang"
                    );
                }

                // 2. JSON конкретной версии — находим прямую ссылку на server.jar
                string versionJson = await DownloadHelper.GetStringAsync(versionUrl);

                using JsonDocument versionDoc = JsonDocument.Parse(versionJson);

                string serverUrl =
                    versionDoc.RootElement
                        .GetProperty("downloads")
                        .GetProperty("server")
                        .GetProperty("url")
                        .GetString()!;

                string save = Path.Combine(serverPath, "server.jar");

                await DownloadHelper.DownloadFileAsync(
                    serverUrl,
                    save,
                    progress => DownloadProgress?.Invoke(progress));
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "Ошибка установки Vanilla: " + ex.Message
                );
            }
        }

        // Список релизных версий (без снапшотов) — для выпадающего списка.
        // Кэшируется, чтобы не тянуть манифест заново при каждом переключении.
        public async Task<List<string>> GetVersions(bool forceRefresh = false)
        {
            return await VersionListCache.GetAsync(
                "vanilla-versions",
                async () =>
                {
                    string json = await DownloadHelper.GetStringAsync(ManifestUrl);

                    using JsonDocument doc = JsonDocument.Parse(json);

                    List<string> list = new();

                    foreach (JsonElement v in doc.RootElement.GetProperty("versions").EnumerateArray())
                    {
                        if (v.GetProperty("type").GetString() == "release")
                        {
                            list.Add(v.GetProperty("id").GetString()!);
                        }
                    }

                    return list;
                },
                forceRefresh);
        }
    }
}
