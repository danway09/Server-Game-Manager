using MCServerManager.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace MCServerManager.Managers
{
    public class TerrariaConsoleManager
    {

        private Process? serverProcess;



        // Свой отдельный Job Object — та же защита от зомби-процессов,
        // что и у Minecraft-сервера
        private static readonly JobObject jobObject = new JobObject();



        public event Action<string>? ConsoleReceived;

        // Сработает, когда сервер реально поднялся и готов принимать игроков
        public event Action? ServerReady;

        // Срабатывает, если процесс завершился САМ, не через нашу же
        // команду StopServer()/KillIfRunning() — похоже на краш
        public event Action? UnexpectedExit;

        // true всё время между началом StopServer()/KillIfRunning() и
        // новым StartServer() — отличает "мы сами остановили" от краша
        private bool intentionalStop = false;



        public bool IsRunning
        {
            get
            {
                return serverProcess != null && !serverProcess.HasExited;
            }
        }








        public void StartServer(TerrariaServer server)
        {

            if (IsRunning)
                return;



            intentionalStop = false;



            bool isTModLoader =
                server.ServerCore.Equals("tModLoader", StringComparison.OrdinalIgnoreCase);



            // ГЛАВНЫЙ ФИКС: SteamCMD (а значит и сам tModLoaderServer.exe,
            // раз он тоже завязан на Steamworks) отказывается работать,
            // если где-либо в пути встречаются не-английские символы —
            // включая кириллицу в имени пользователя Windows. Поэтому для
            // tModLoader используем отдельную безопасную папку на C:\
            // вместо обычной server.Path (которая может лежать внутри
            // C:\Users\Артём\... и т.п.)
            string workingDir =
                isTModLoader
                    ? TModLoaderPaths.GetServerInstallDir(server.Name)
                    : server.Path;



            string? exePath =
                Directory.GetFiles(workingDir, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f =>
                        isTModLoader
                            ? Path.GetFileName(f).Contains("tModLoader", StringComparison.OrdinalIgnoreCase)
                            : Path.GetFileName(f).Contains("TShock", StringComparison.OrdinalIgnoreCase) ||
                              Path.GetFileName(f).Contains("Server", StringComparison.OrdinalIgnoreCase)
                    );



            if (exePath == null)
            {

                throw new Exception(
                    $"Не найден exe-файл {server.ServerCore} в папке \"{workingDir}\". " +
                    "Сначала установи его через кнопку установки."
                );

            }



            string worldPath =
                Path.Combine(workingDir, server.WorldName + ".wld");



            StringBuilder args = new StringBuilder();



            args.Append($"-port {server.Port} ");

            args.Append($"-maxplayers {server.MaxPlayers} ");

            args.Append($"-difficulty {server.Difficulty} ");

            args.Append($"-world \"{worldPath}\" ");



            // Мира ещё нет — просим TShock/Terraria создать его
            // автоматически с нужным размером и именем, вместо того чтобы
            // ждать интерактивных вопросов в консоли (которых мы не увидим)
            if (!File.Exists(worldPath))
            {

                args.Append($"-autocreate {server.WorldSize} ");

                args.Append($"-worldname \"{server.WorldName}\" ");

            }



            if (!string.IsNullOrEmpty(server.Password))
            {
                args.Append($"-password \"{server.Password}\" ");
            }



            if (!string.IsNullOrEmpty(server.Motd))
            {
                args.Append($"-motd \"{server.Motd}\" ");
            }



            // У tModLoader моды лежат в отдельной папке — указываем явно,
            // чтобы всё оставалось внутри папки конкретного сервера, а не
            // расползалось по Documents\My Games\Terraria\tModLoader
            if (isTModLoader)
            {

                string modsPath =
                    Path.Combine(workingDir, "Mods");



                Directory.CreateDirectory(modsPath);



                args.Append($"-modpath \"{modsPath}\" ");

            }



            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args.ToString().Trim(),
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // ГЛАВНЫЙ ФИКС: без явного указания кодировки консоль TShock
                // читалась не в той кодировке — русский текст превращался
                // в кракозябры вида "РЎР±СЂРѕСЃ" вместо "Сброс"
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8
            };



            serverProcess = new Process();

            serverProcess.StartInfo = info;

            serverProcess.EnableRaisingEvents = true;


            serverProcess.Exited +=
                (s, e) =>
                {

                    if (!intentionalStop)
                    {
                        UnexpectedExit?.Invoke();
                    }

                };



            serverProcess.OutputDataReceived +=
                (s, e) =>
                {

                    if (e.Data != null)
                    {

                        ConsoleReceived?.Invoke(e.Data);



                        // Точную формулировку у разных версий TShock лучше
                        // будет свериться при первом реальном запуске и
                        // подправить при необходимости
                        if (e.Data.Contains("Type 'help'", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("Server started", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("Listening on port", StringComparison.OrdinalIgnoreCase))
                        {

                            ServerReady?.Invoke();

                        }

                    }

                };



            serverProcess.ErrorDataReceived +=
                (s, e) =>
                {

                    if (e.Data != null)
                    {
                        ConsoleReceived?.Invoke(e.Data);
                    }

                };



            serverProcess.Start();



            jobObject.AddProcess(serverProcess.Handle);



            serverProcess.BeginOutputReadLine();

            serverProcess.BeginErrorReadLine();

        }








        public void SendCommand(string command)
        {

            if (!IsRunning)
                return;



            serverProcess!.StandardInput.WriteLine(command);

            serverProcess.StandardInput.Flush();

        }








        public void StopServer()
        {

            if (!IsRunning)
                return;



            intentionalStop = true;



            SendCommand("exit");



            bool exited =
                serverProcess!.WaitForExit(10000);



            if (!exited)
            {

                try
                {

                    serverProcess.Kill(entireProcessTree: true);


                    serverProcess.WaitForExit(5000);

                }
                catch
                {
                }

            }



            serverProcess.Dispose();

            serverProcess = null;

        }








        public void KillIfRunning()
        {

            if (serverProcess == null)
                return;



            intentionalStop = true;



            try
            {

                if (!serverProcess.HasExited)
                {
                    serverProcess.Kill(entireProcessTree: true);
                }

            }
            catch
            {
            }
            finally
            {
                serverProcess.Dispose();
                serverProcess = null;
            }

        }

    }
}