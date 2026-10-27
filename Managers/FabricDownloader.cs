using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    public class FabricDownloader
    {

        // сетевые операции идут через общий DownloadHelper.SharedClient








        // установка Fabric сервера

        public async Task InstallFabric(
            string serverPath,
            string minecraftVersion)
        {

            try
            {

                string installer =
                    Path.Combine(
                        serverPath,
                        "fabric-installer.jar"
                    );



                // ГЛАВНЫЙ ФИКС: раньше тут был захардкожен URL конкретной
                // старой версии инсталлятора (1.0.1/0.11.2), которая со
                // временем исчезла с сервера Fabric — отсюда 404.
                // Теперь версия инсталлятора запрашивается динамически.
                string installerVersion =
                    await GetLatestInstallerVersion();



                string url =
                    "https://maven.fabricmc.net/net/fabricmc/fabric-installer/" +
                    $"{installerVersion}/fabric-installer-{installerVersion}.jar";




                await DownloadHelper.DownloadFileAsync(url, installer);

                // файл гарантированно закрыт к этому моменту — теперь безопасно
                // запускать java на нём



                await RunInstaller(
                    serverPath,
                    installer,
                    minecraftVersion
                );



                FileHelper.TryDeleteWithRetries(installer);



                // ГЛАВНЫЙ ФИКС: наш движок запуска всегда ждёт файл именно
                // с именем server.jar, а инсталлятор Fabric в зависимости от
                // версии может назвать итоговый файл "fabric-server-launch.jar".
                // Проверяем и переименовываем, если нужно, вместо того чтобы
                // молча "успешно" завершиться, а потом не суметь запустить сервер.
                string expectedJar =
                    Path.Combine(serverPath, "server.jar");



                if (!File.Exists(expectedJar))
                {

                    string altJar =
                        Path.Combine(serverPath, "fabric-server-launch.jar");



                    if (File.Exists(altJar))
                    {

                        File.Move(altJar, expectedJar);

                    }
                    else
                    {

                        throw new Exception(
                            "Инсталлятор завершился без ошибок, но ни server.jar, " +
                            "ни fabric-server-launch.jar не появились."
                        );

                    }

                }


            }
            catch (Exception ex)
            {

                throw new Exception(
                    "Ошибка установки Fabric: "
                    + ex.Message
                );

            }

        }








        // Берёт самую свежую версию Fabric Installer с официального API —
        // список возвращается от новых к старым, первая запись — самая новая
        private async Task<string> GetLatestInstallerVersion()
        {

            string json =
                await DownloadHelper.GetStringAsync(
                    "https://meta.fabricmc.net/v2/versions/installer"
                );



            using JsonDocument doc =
                JsonDocument.Parse(json);



            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {

                return item.GetProperty("version").GetString()!;

            }



            throw new Exception(
                "Не удалось получить версию Fabric Installer"
            );

        }








        // Список поддерживаемых версий Minecraft — для выпадающего списка версий
        public async Task<List<string>> GetGameVersions()
        {

            string json =
                await DownloadHelper.GetStringAsync(
                    "https://meta.fabricmc.net/v2/versions/game"
                );



            using JsonDocument doc =
                JsonDocument.Parse(json);



            List<string> list = new();



            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {

                bool stable =
                    item.TryGetProperty("stable", out JsonElement stableEl) &&
                    stableEl.GetBoolean();



                if (stable)
                {

                    list.Add(item.GetProperty("version").GetString()!);

                }

            }



            return list;

        }








        private async Task RunInstaller(
            string serverPath,
            string installer,
            string version)
        {

            var info =
                new System.Diagnostics.ProcessStartInfo();


            info.FileName =
                "java";



            // ГЛАВНЫЙ ФИКС: та же проблема задвоения пути, что у Forge/NeoForge —
            // WorkingDirectory уже указывает на папку сервера, нужно только имя файла
            info.Arguments =
                $"-jar \"{Path.GetFileName(installer)}\" server -mcversion {version} -downloadMinecraft";



            info.WorkingDirectory =
                serverPath;



            info.UseShellExecute =
                false;



            info.CreateNoWindow =
                true;



            info.RedirectStandardOutput =
                true;


            info.RedirectStandardError =
                true;




            var process =
                System.Diagnostics.Process.Start(info);



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
                    string.IsNullOrWhiteSpace(error) ? output : error;



                if (details.Length > 1500)
                {
                    details = "..." + details.Substring(details.Length - 1500);
                }



                throw new Exception(
                    "Установщик Fabric завершился с ошибкой (код " +
                    process.ExitCode + ").\n\nВывод установщика:\n" + details
                );

            }



        }






        // проверка Fabric

        public bool HasFabric(string path)
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