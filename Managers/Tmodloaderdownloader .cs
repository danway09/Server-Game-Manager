using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // tModLoader распространяется только через Steam — готового jar/zip
    // для прямого скачивания, как у Paper или Fabric, тут просто нет.
    // Официальный способ получить именно ВЫДЕЛЕННЫЙ сервер (без графики) —
    // SteamCMD с AppID 1281930.
    //
    // ВАЖНО: SteamCMD отказывается работать, если где-либо в полном пути
    // встречаются не-английские символы (в т.ч. кириллица в имени
    // пользователя Windows). Поэтому ставим не в папку самого сервера
    // (которая может лежать внутри C:\Users\Имя_с_кириллицей\...), а в
    // отдельный фиксированный ASCII-путь — см. TModLoaderPaths.
    public class TModLoaderDownloader
    {

        private readonly SteamCmdDownloader steamCmd = new();



        private const int TModLoaderServerAppId = 1281930;



        // Возвращает реальную папку установки (на C:\, без кириллицы) —
        // она нужна и для запуска сервера, и для работы с миром/модами
        public async Task<string> InstallTModLoader(string serverName, Action<string>? onOutput = null)
        {

            string installDir =
                TModLoaderPaths.GetServerInstallDir(serverName);



            try
            {

                await steamCmd.EnsureInstalled(onOutput);



                onOutput?.Invoke(
                    "Скачиваю tModLoader через SteamCMD — может занять несколько минут " +
                    "в зависимости от интернета, просто подожди."
                );



                onOutput?.Invoke("Устанавливаю в " + installDir + " (без кириллицы — так требует SteamCMD)");



                // ГЛАВНЫЙ ФИКС #2: SteamCMD — известно капризная штука,
                // сообщество давно в курсе, что app_update иногда нужно
                // прогнать ДВАЖДЫ подряд (первый раз может вернуть "Timed
                // out waiting for update to start" и ложный "Success!",
                // хотя реально ничего не скачалось). Пробуем автоматически
                // ещё раз, прежде чем сдаться.
                string? exePath = null;

                int exitCode = 0;

                string output = "";



                for (int attempt = 1; attempt <= 2; attempt++)
                {

                    if (attempt == 2)
                    {

                        onOutput?.Invoke(
                            "С первого раза файлы не появились — это известная " +
                            "особенность SteamCMD, пробую ещё раз автоматически..."
                        );

                    }



                    (exitCode, output) =
                        await RunSteamCmd(installDir, onOutput);



                    exePath =
                        Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly)
                            .FirstOrDefault(f =>
                                Path.GetFileName(f).Contains("tModLoader", StringComparison.OrdinalIgnoreCase)
                            );



                    if (exePath != null)
                    {
                        break;
                    }

                }



                // Папка для модов — сюда потом будет класть .tmod файлы
                Directory.CreateDirectory(
                    Path.Combine(installDir, "Mods")
                );



                // ГЛАВНЫЙ ФИКС: SteamCMD после "Update complete" иногда
                // пытается разок ЗАПУСТИТЬ только что установленное
                // приложение как часть проверки — и эта попытка запуска
                // может упасть с ненулевым кодом, хотя сами файлы игры уже
                // полностью на месте. Поэтому ориентируемся не на код
                // выхода, а на факт: появился ли реально tModLoaderServer.exe.
                if (exePath == null)
                {

                    string details = output;



                    if (details.Length > 1500)
                    {
                        details = "..." + details.Substring(details.Length - 1500);
                    }



                    throw new Exception(
                        $"SteamCMD завершился (код {exitCode}) дважды, но " +
                        "tModLoaderServer.exe в папке установки так и не появился.\n\n" +
                        "Вывод:\n" + details
                    );

                }



                if (exitCode != 0)
                {

                    onOutput?.Invoke(
                        $"SteamCMD вернул код {exitCode}, но файлы игры на месте — " +
                        "считаю установку успешной (это известная особенность SteamCMD)."
                    );

                }



                onOutput?.Invoke("Готово! tModLoader установлен в " + installDir);



                return installDir;

            }
            catch (Exception ex)
            {

                throw new Exception(
                    "Ошибка установки tModLoader: " + ex.Message +
                    "\n\ntModLoader ставится через официальный SteamCMD — " +
                    "нужен стабильный интернет, установка не быстрая."
                );

            }

        }








        private async Task<(int exitCode, string output)> RunSteamCmd(string installDir, Action<string>? onOutput)
        {

            string steamCmdExeFull =
                Path.GetFullPath(steamCmd.SteamCmdExePath);



            Directory.CreateDirectory(installDir);



            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = steamCmdExeFull,
                Arguments =
                    $"+force_install_dir \"{installDir}\" " +
                    "+login anonymous " +
                    $"+app_update {TModLoaderServerAppId} validate " +
                    "+quit",
                WorkingDirectory = Path.GetDirectoryName(steamCmdExeFull),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };



            var process =
                Process.Start(info);



            StringBuilder fullOutput =
                new StringBuilder();



            process!.OutputDataReceived +=
                (s, e) =>
                {

                    if (e.Data != null)
                    {

                        fullOutput.AppendLine(e.Data);


                        onOutput?.Invoke(e.Data);

                    }

                };



            process.ErrorDataReceived +=
                (s, e) =>
                {

                    if (e.Data != null)
                    {

                        fullOutput.AppendLine(e.Data);


                        onOutput?.Invoke(e.Data);

                    }

                };



            process.BeginOutputReadLine();

            process.BeginErrorReadLine();



            await process.WaitForExitAsync();



            return (process.ExitCode, fullOutput.ToString());

        }

    }
}