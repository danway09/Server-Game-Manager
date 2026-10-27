using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // SteamCMD — официальный консольный инструмент Valve для скачивания
    // выделенных серверов через Steam. Ставим один раз в общую папку
    // (см. TModLoaderPaths — фиксированный ASCII-путь на C:\, не рядом
    // с программой) — экономит место и время, все tModLoader-сервера
    // используют одну и ту же копию.
    public class SteamCmdDownloader
    {

        private readonly HttpClient client = new HttpClient();



        private const string SteamCmdUrl =
            "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";



        public string SteamCmdExePath =>
            Path.Combine(TModLoaderPaths.SteamCmdFolder, "steamcmd.exe");



        public async Task EnsureInstalled(Action<string>? onOutput = null)
        {

            if (File.Exists(SteamCmdExePath))
            {

                onOutput?.Invoke("SteamCMD уже установлен.");


                return;

            }



            Directory.CreateDirectory(TModLoaderPaths.SteamCmdFolder);



            onOutput?.Invoke("Скачиваю SteamCMD (один раз, дальше будет использоваться готовый)...");



            string zipPath =
                Path.Combine(TModLoaderPaths.SteamCmdFolder, "steamcmd.zip");



            using (HttpResponseMessage response = await client.GetAsync(SteamCmdUrl))
            {

                response.EnsureSuccessStatusCode();



                await using FileStream fs =
                    new FileStream(zipPath, FileMode.Create);



                await response.Content.CopyToAsync(fs);

            }



            ZipFile.ExtractToDirectory(zipPath, TModLoaderPaths.SteamCmdFolder, overwriteFiles: true);



            FileHelper.TryDeleteWithRetries(zipPath);



            onOutput?.Invoke("SteamCMD установлен.");

        }

    }
}