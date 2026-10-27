using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;

namespace MCServerManager.Managers
{
    public class ConsoleManager
    {

        private Process? serverProcess;


        // Job Object живёт всё время работы приложения. Он гарантирует,
        // что java.exe умрёт вместе с приложением, даже если его убили
        // через "Стоп" в Visual Studio, крестиком, крашем и т.п. —
        // именно это раньше оставляло зомби-процессы, блокирующие latest.log
        private static readonly JobObject jobObject = new JobObject();


        public event Action<string>? ConsoleReceived;

        // Срабатывает, когда сервер реально дошёл до "Done (...)!" —
        // то есть полностью загрузился и готов принимать подключения
        public event Action? ServerReady;



        public bool IsRunning
        {
            get
            {
                return serverProcess != null &&
                       !serverProcess.HasExited;
            }
        }

        public Action<string>? ConsoleMessage { get; internal set; }
        public Action UnexpectedExit { get; internal set; }

        public void StartServer(
            string serverPath,
            int ram,
            bool onlineMode = true,
            string core = "Paper",
            string version = "")
        {

            if (IsRunning)
                return;



            // Forge и NeoForge запускаются иначе, чем остальные ядра: новые
            // сборки (примерно с 1.17) генерируют run.bat/run.sh (они сами
            // подставляют нужные библиотеки), а старые (1.12.2–1.16.5) кладут
            // forge-*.jar (или переименованный в server.jar) — запускаем как jar.
            bool isForgeLike =
                core.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
                core.Equals("NeoForge", StringComparison.OrdinalIgnoreCase);

            string? runScriptPath = null;
            string? legacyForgeJar = null;

            if (isForgeLike)
            {
                string runScript =
                    Path.Combine(serverPath, "run.bat");

                if (File.Exists(runScript))
                {
                    runScriptPath = runScript;
                }
                else if (core.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                {
                    legacyForgeJar = ForgeInstallStatus.ResolveForgeLaunchJar(serverPath);
                }

                if (runScriptPath == null && legacyForgeJar == null)
                {
                    throw new Exception(
                                                "Ядро " + core + " не установлено: в папке нет ни run.bat, " +
                        "ни серверного jar (forge-*.jar или server.jar). " +
                        "Сначала установи ядро через кнопку \"Установить ядро\"."
                    );
                }
            }
            else
            {
                string jar =
                    Path.Combine(serverPath, "server.jar");

                if (!File.Exists(jar))
                {
                    throw new Exception(
                        "server.jar не найден!"
                    );
                }
            }


            // Minecraft-сервер откажется запускаться без принятого EULA
            // (создаст/проверит eula.txt, если нужно — спросит подтверждение)
            EnsureEulaAccepted(serverPath);


            // Проверяем порт ДО запуска java — иначе процесс всё равно
            // стартует, но упадёт с невнятным крэш-репортом внутри консоли,
            // а зависший java.exe останется висеть и держать порт дальше
            EnsurePortAvailable(serverPath);


            // Прописываем online-mode в server.properties под выбранный режим
            // (пиратский клиент не пройдёт проверку Mojang при online-mode=true)
            ApplyOnlineMode(serverPath, onlineMode);




            // Старые версии Minecraft (примерно до 1.16.5 включительно)
            // не запускаются на новой Java — подбираем подходящую (обычно
            // Java 8) из стандартных мест установки; иначе java из PATH.
            string launchJava =
                JavaRuntime.ResolveLaunchJava(version);

            ProcessStartInfo info =
                new ProcessStartInfo();


            if (runScriptPath != null)
            {

                // У новых Forge/NeoForge ОЗУ настраивается через отдельный файл
                // user_jvm_args.txt, а не через -Xmx в командной строке
                ApplyForgeRam(serverPath, ram);

                info.FileName =
                    "cmd.exe";

                info.Arguments =
                    "/c run.bat nogui";

            }
            else if (legacyForgeJar != null)
            {

                info.FileName =
                    launchJava;

                info.Arguments =
                    $"-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 " +
                    $"-Xmx{ram}M -Xms{ram}M -jar \"{Path.GetFileName(legacyForgeJar)}\" nogui";

            }
            else
            {

                info.FileName =
                    launchJava;

                info.Arguments =
                    $"-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 " +
                    $"-Xmx{ram}M -Xms{ram}M -jar server.jar nogui";

            }


            info.WorkingDirectory =
                serverPath;


            info.UseShellExecute =
                false;


            info.RedirectStandardInput =
                true;


            info.RedirectStandardOutput =
                true;


            info.RedirectStandardError =
                true;


            info.CreateNoWindow =
                true;


            // Читаем вывод процесса тоже как UTF-8, соответствуя флагам выше
            info.StandardOutputEncoding =
                Encoding.UTF8;


            info.StandardErrorEncoding =
                Encoding.UTF8;





            serverProcess =
                new Process();


            serverProcess.StartInfo =
                info;


            // Чтобы процесс не оставался сиротой при крэше/закрытии приложения
            serverProcess.EnableRaisingEvents =
                true;





            serverProcess.OutputDataReceived +=
                (s, e) =>
                {

                    if (e.Data != null)
                    {
                        ConsoleReceived?.Invoke(
                            e.Data
                        );



                        // "Done (4.238s)! For help, type "help"" — стандартная
                        // строка, которую печатает ЛЮБОЕ ядро (Vanilla, Paper,
                        // Purpur, Fabric, Forge, NeoForge), когда сервер
                        // полностью загрузился и готов принимать подключения
                        if (e.Data.Contains("Done (") && e.Data.Contains("For help"))
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
                        ConsoleReceived?.Invoke(
                            e.Data
                        );
                    }

                };





            serverProcess.Start();


            // Привязываем java.exe к Job Object — теперь ОС сама убьёт его,
            // если наше приложение умрёт любым способом
            jobObject.AddProcess(serverProcess.Handle);


            serverProcess.BeginOutputReadLine();

            serverProcess.BeginErrorReadLine();

        }








        // Проверяет eula.txt в папке сервера. Если EULA ещё не принят —
        // спрашивает подтверждение у пользователя и, если тот согласен,
        // сам прописывает eula=true. Без этого Minecraft-сервер (Vanilla,
        // Fabric, Paper — любое ядро) откажется стартовать.
        private void EnsureEulaAccepted(string serverPath)
        {

            string eulaPath =
                Path.Combine(serverPath, "eula.txt");



            bool alreadyAccepted = false;



            if (File.Exists(eulaPath))
            {

                string existing =
                    File.ReadAllText(eulaPath);


                alreadyAccepted =
                    existing.Contains(
                        "eula=true",
                        StringComparison.OrdinalIgnoreCase
                    );

            }



            if (alreadyAccepted)
                return;



            MessageBoxResult result =
                MessageBox.Show(
                    "Для запуска сервера Minecraft нужно принять лицензионное " +
                    "соглашение Mojang (EULA).\n\n" +
                    "Ознакомиться можно здесь: https://www.minecraft.net/eula\n\n" +
                    "Принять EULA и продолжить запуск сервера?",
                    "Требуется принятие EULA",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );



            if (result != MessageBoxResult.Yes)
            {

                throw new Exception(
                    "Запуск отменён: EULA не принят."
                );

            }



            string eulaContent =
                "#By changing the setting below to TRUE you are indicating " +
                "your agreement to our EULA (https://aka.ms/MinecraftEULA).\n" +
                "#" + DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy") + "\n" +
                "eula=true\n";



            File.WriteAllText(
                eulaPath,
                eulaContent
            );

        }








        // Прописывает (или создаёт) строку online-mode в server.properties.
        // Если файла ещё нет (первый запуск, до генерации ядром) — создаёт
        // минимальный файл с этой настройкой, ядро дозаполнит остальное само.
        private void ApplyOnlineMode(string serverPath, bool onlineMode)
        {

            string propsPath =
                Path.Combine(serverPath, "server.properties");



            string valueLine =
                "online-mode=" + (onlineMode ? "true" : "false");



            if (!File.Exists(propsPath))
            {

                File.WriteAllText(
                    propsPath,
                    valueLine + Environment.NewLine
                );


                return;

            }



            string[] lines =
                File.ReadAllLines(propsPath);



            bool found = false;



            for (int i = 0; i < lines.Length; i++)
            {

                if (lines[i].StartsWith("online-mode="))
                {

                    lines[i] = valueLine;


                    found = true;


                    break;

                }

            }



            if (found)
            {

                File.WriteAllLines(propsPath, lines);

            }
            else
            {

                File.AppendAllText(
                    propsPath,
                    Environment.NewLine + valueLine + Environment.NewLine
                );

            }

        }








        // Прописывает объём ОЗУ в user_jvm_args.txt — именно там Forge и
        // NeoForge читают -Xmx/-Xms при запуске через run.bat, командная
        // строка тут игнорируется
        private void ApplyForgeRam(string serverPath, int ramMb)
        {

            string path =
                Path.Combine(serverPath, "user_jvm_args.txt");



            List<string> lines =
                File.Exists(path)
                    ? new List<string>(File.ReadAllLines(path))
                    : new List<string>();



            lines.RemoveAll(
                l => l.TrimStart().StartsWith("-Xmx") ||
                     l.TrimStart().StartsWith("-Xms")
            );



            lines.Add($"-Xmx{ramMb}M");

            lines.Add($"-Xms{ramMb}M");



            File.WriteAllLines(path, lines);

        }




        // Читает server-port из server.properties (по умолчанию 25565,
        // как у ванильного/фабрик-сервера, если файла ещё нет или строки нет)
        private int ReadServerPort(string serverPath)
        {

            string propsPath =
                Path.Combine(serverPath, "server.properties");



            int port = 25565;



            if (File.Exists(propsPath))
            {

                foreach (string line in File.ReadAllLines(propsPath))
                {

                    if (line.StartsWith("server-port="))
                    {

                        string value =
                            line.Substring("server-port=".Length).Trim();


                        if (int.TryParse(value, out int parsed))
                        {
                            port = parsed;
                        }


                        break;

                    }

                }

            }



            return port;

        }








        // Пробует занять порт сам — если не получается, значит порт уже
        // занят другим процессом. Если это старый зависший java.exe (самая
        // частая причина), убиваем его автоматически и пробуем ещё раз.
        // Ничего, кроме java.exe, автоматически не трогаем — чтобы случайно
        // не прибить что-то системное, занявшее тот же порт.
        private void EnsurePortAvailable(string serverPath)
        {

            int port =
                ReadServerPort(serverPath);



            if (IsPortFree(port))
                return;



            bool freedAutomatically =
                TryAutoKillPortHolder(port);



            if (freedAutomatically && IsPortFree(port))
                return;



            throw new Exception(
                $"Порт {port} уже занят другим процессом, и автоматически " +
                "освободить его не получилось.\n\n" +
                $"Проверь вручную через 'netstat -ano | findstr :{port}' " +
                "и заверши процесс, который его держит, либо смени " +
                "server-port в server.properties."
            );

        }








        private bool IsPortFree(int port)
        {

            TcpListener? listener = null;



            try
            {

                listener =
                    new TcpListener(IPAddress.Any, port);


                listener.Start();


                return true;

            }
            catch (SocketException)
            {

                return false;

            }
            finally
            {

                listener?.Stop();

            }

        }








        // Находит через netstat все PID, слушающие указанный TCP-порт
        private System.Collections.Generic.List<int> FindPidsListeningOnPort(int port)
        {

            var pids =
                new System.Collections.Generic.List<int>();



            try
            {

                var psi = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano -p TCP",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };



                using var proc =
                    Process.Start(psi);



                if (proc == null)
                    return pids;



                string output =
                    proc.StandardOutput.ReadToEnd();


                proc.WaitForExit(3000);



                foreach (string rawLine in output.Split('\n'))
                {

                    string line =
                        rawLine.Trim();



                    if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
                        continue;



                    string[] parts =
                        Regex.Split(line, @"\s+");



                    if (parts.Length < 5)
                        continue;



                    string localAddress = parts[1];
                    string state = parts[3];
                    string pidText = parts[4];



                    if (!state.Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                        continue;



                    if (!localAddress.EndsWith(":" + port))
                        continue;



                    if (int.TryParse(pidText, out int pid))
                    {
                        pids.Add(pid);
                    }

                }

            }
            catch
            {
                // netstat недоступен по какой-то причине — просто не найдём PID,
                // выше это приведёт к обычной ошибке "порт занят"
            }



            return pids;

        }








        // Убивает процессы, держащие порт, но ТОЛЬКО если это java.exe —
        // так безопаснее, чем гасить произвольный системный процесс
        private bool TryAutoKillPortHolder(int port)
        {

            var pids =
                FindPidsListeningOnPort(port);



            bool killedAnyJava = false;



            foreach (int pid in pids)
            {

                try
                {

                    Process proc =
                        Process.GetProcessById(pid);



                    if (proc.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
                    {

                        proc.Kill(entireProcessTree: true);


                        proc.WaitForExit(5000);


                        killedAnyJava = true;

                    }

                }
                catch
                {
                    // процесс уже мог сам исчезнуть между поиском и Kill — не страшно
                }

            }



            if (!killedAnyJava)
                return false;



            // даём Windows секунду, чтобы фактически освободить порт после Kill
            Thread.Sleep(1000);



            return true;

        }








        public void SendCommand(
            string command)
        {

            if (!IsRunning)
                return;



            serverProcess!
                .StandardInput
                .WriteLine(command);



            serverProcess
                .StandardInput
                .Flush();

        }








        public void StopServer()
        {

            if (!IsRunning)
                return;



            SendCommand(
                "stop"
            );



            bool exited =
                serverProcess!.WaitForExit(10000);



            // ГЛАВНЫЙ ФИКС: если сервер не успел закрыться сам за 10 секунд —
            // раньше код просто делал Dispose()/null и "забывал" про процесс,
            // а java.exe оставался висеть в фоне и блокировать logs/latest.log
            // при следующем запуске. Теперь принудительно убиваем его.
            if (!exited)
            {

                try
                {

                    serverProcess.Kill(
                        entireProcessTree: true
                    );


                    serverProcess.WaitForExit(5000);

                }
                catch
                {
                    // процесс уже мог сам завершиться в этот момент — игнорируем
                }

            }



            serverProcess.Dispose();


            serverProcess = null;

        }








        // Аварийная остановка — вызывать при закрытии окна/приложения,
        // чтобы java.exe гарантированно не оставался висеть в фоне
        public void KillIfRunning()
        {

            if (serverProcess == null)
                return;



            try
            {

                if (!serverProcess.HasExited)
                {
                    serverProcess.Kill(entireProcessTree: true);
                }

            }
            catch
            {
                // ignore
            }
            finally
            {
                serverProcess.Dispose();
                serverProcess = null;
            }

        }

    }
}