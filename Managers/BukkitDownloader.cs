using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // Bukkit (CraftBukkit) — единственное ядро, которое не скачивается
    // готовым файлом, а СОБИРАЕТСЯ из исходников через официальный
    // BuildTools от SpigotMC. Это медленно (5-15 минут) и требует Git.
    public class BukkitDownloader
    {

        // сетевые операции идут через общий DownloadHelper.SharedClient



        private const string BuildToolsUrl =
            "https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/artifact/target/BuildTools.jar";



        public async Task InstallBukkit(
            string serverPath,
            string minecraftVersion,
            Action<string>? onOutput = null)
        {

            try
            {

                EnsureGitInstalled();



                onOutput?.Invoke("Скачиваю BuildTools.jar...");



                string buildToolsPath =
                    Path.Combine(serverPath, "BuildTools.jar");



                await DownloadHelper.DownloadFileAsync(BuildToolsUrl, buildToolsPath);



                onOutput?.Invoke(
                    "BuildTools скачан. Начинаю сборку CraftBukkit под версию " +
                    minecraftVersion + " — это может занять 5-15 минут..."
                );



                await RunBuildTools(serverPath, minecraftVersion, onOutput);



                FileHelper.TryDeleteWithRetries(buildToolsPath);



                string? builtJar =
                    Directory.GetFiles(serverPath, "craftbukkit-*.jar")
                        .FirstOrDefault();



                if (builtJar == null)
                {

                    throw new Exception(
                        "BuildTools завершился без ошибок, но файл craftbukkit-*.jar не найден."
                    );

                }



                string finalPath =
                    Path.Combine(serverPath, "server.jar");



                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }



                File.Move(builtJar, finalPath);



                onOutput?.Invoke("Готово! server.jar собран.");

            }
            catch (Exception ex)
            {

                throw new Exception(
                    "Ошибка установки Bukkit: " + ex.Message +
                    "\n\nBukkit собирается из исходников через официальный " +
                    "BuildTools — нужен установленный Git и стабильный " +
                    "интернет, сборка не быстрая."
                );

            }

        }








        private void EnsureGitInstalled()
        {

            try
            {

                var info = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };



                using var process =
                    Process.Start(info);



                process!.WaitForExit(5000);



                if (process.ExitCode != 0)
                {
                    throw new Exception();
                }

            }
            catch
            {

                throw new Exception(
                    "Git не найден. BuildTools (сборщик Bukkit) не может " +
                    "работать без Git. Установи его с https://git-scm.com/ " +
                    "и попробуй снова."
                );

            }

        }








        private async Task RunBuildTools(
            string serverPath,
            string minecraftVersion,
            Action<string>? onOutput)
        {

            var info = new ProcessStartInfo
            {
                FileName = "java",
                Arguments =
                    $"-jar \"BuildTools.jar\" --rev {minecraftVersion} " +
                    "--compile CRAFTBUKKIT --output-dir \".\"",
                WorkingDirectory = serverPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };



            var process =
                Process.Start(info);



            System.Text.StringBuilder fullOutput =
                new System.Text.StringBuilder();



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



            if (process.ExitCode != 0)
            {

                string details =
                    fullOutput.ToString();



                if (details.Length > 1500)
                {
                    details = "..." + details.Substring(details.Length - 1500);
                }



                throw new Exception(
                    "BuildTools завершился с ошибкой (код " + process.ExitCode +
                    ").\n\nВывод:\n" + details
                );

            }

        }








        public async Task<List<string>> GetApproximateSupportedVersions()
        {

            var vanilla = new VanillaDownloader();



            return await vanilla.GetVersions();

        }

    }
}
