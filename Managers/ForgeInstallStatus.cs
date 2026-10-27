using System;
using System.IO;
using System.Text;

namespace MCServerManager.Managers
{
    // Общие правила: что считать установленным ядром Forge и чем его
    // запускать. Новые сборки Forge (примерно с 1.17) кладут run.bat/run.sh,
    // а старые (1.12.2–1.16.5 и древнее) — forge-<mc>-<версия>.jar
    // (+ -universal.jar) и libraries/, но БЕЗ run.bat. Раньше проверка
    // «есть run.bat?» валила такие успешные установки — поэтому правила
    // живут в одном месте и используются и установкой, и запуском сервера.
    public static class ForgeInstallStatus
    {
        public static bool HasRunScript(string serverPath)
        {
            return File.Exists(Path.Combine(serverPath, "run.bat"))
                || File.Exists(Path.Combine(serverPath, "run.sh"));
        }

        // Старый запускаемый jar Forge: forge-*.jar без инсталлятора.
        // Основной серверный файл — не -universal; universal держим как запас.
        public static string? FindLegacyLaunchJar(string serverPath)
        {
            string[] files;

            try
            {
                files = Directory.GetFiles(serverPath, "forge-*.jar");
            }
            catch
            {
                return null;
            }

            string? universal = null;

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);

                if (name.EndsWith("-installer.jar", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.EndsWith("-universal.jar", StringComparison.OrdinalIgnoreCase))
                {
                    universal ??= file;
                    continue;
                }

                return file;
            }

            return universal;
        }

        public static bool IsInstalled(string serverPath)
        {
            // Признаки завершённой установки: run.bat/run.sh (новые сборки),
            // forge-*.jar (старые сборки) или библиотеки Forge (слабый,
            // но полезный признак на случай нестандартных имён файлов).
            return HasRunScript(serverPath)
                || FindLegacyLaunchJar(serverPath) != null
                || HasForgeLibraries(serverPath);
        }

        public static bool HasForgeLibraries(string serverPath)
        {
            string libs = Path.Combine(serverPath, "libraries", "net", "minecraftforge", "forge");

            return Directory.Exists(libs);
        }

        // Чем запускать Forge-сервер (когда run.bat нет): старый
        // forge-*.jar или server.jar, если ядро переименовали в него —
        // но только при наличии библиотек Forge, чтобы не запустить
        // случайный не-форджевый server.jar.
        public static string? ResolveForgeLaunchJar(string serverPath)
        {
            string? legacy = FindLegacyLaunchJar(serverPath);

            if (legacy != null)
            {
                return legacy;
            }

            string serverJar = Path.Combine(serverPath, "server.jar");

            if (File.Exists(serverJar) && HasForgeLibraries(serverPath))
            {
                return serverJar;
            }

            return null;
        }

        // Диагностика для сообщений об ошибке: что реально лежит в папке.
        public static string DescribeDirectory(string serverPath)
        {
            StringBuilder builder = new StringBuilder();

            try
            {
                int shown = 0;

                foreach (string dir in Directory.GetDirectories(serverPath))
                {
                    if (shown >= 30)
                    {
                        break;
                    }

                    builder.AppendLine("  [папка] " + Path.GetFileName(dir));

                    shown++;
                }

                if (shown < 30)
                {
                    foreach (string file in Directory.GetFiles(serverPath))
                    {
                        if (shown >= 30)
                        {
                            break;
                        }

                        builder.AppendLine("  " + Path.GetFileName(file));

                        shown++;
                    }
                }

                if (shown >= 30)
                {
                    builder.AppendLine("  ...");
                }

                if (shown == 0)
                {
                    builder.AppendLine("  (папка пуста)");
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine("  (не удалось прочитать список файлов: " + ex.Message + ")");
            }

            return builder.ToString();
        }
    }
}
