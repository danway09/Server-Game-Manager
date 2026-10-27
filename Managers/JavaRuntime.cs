using System;
using System.IO;

namespace MCServerManager.Managers
{
    // Подбор Java для запуска сервера. Старые версии Minecraft (примерно
    // до 1.16.5 включительно) не работают на новой Java — им нужна
    // Java 8 (или хотя бы не 21+). Ищем её в стандартных местах установки;
    // если не нашли — используется java из PATH.
    public static class JavaRuntime
    {
        public static string ResolveLaunchJava(string minecraftVersion)
        {
            if (IsLegacyMinecraft(minecraftVersion))
            {
                string? legacy = FindLegacyJava();

                if (legacy != null)
                {
                    return legacy;
                }
            }

            return "java";
        }

        // "1.x" с минорной версией <= 16 — старая эпоха (1.16.5 и раньше).
        // Новые схемы версий (например, "26.1.2") считаются современными.
        public static bool IsLegacyMinecraft(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            string[] parts = version.Split('.');

            if (parts.Length >= 2 &&
                parts[0] == "1" &&
                int.TryParse(parts[1], out int minor))
            {
                return minor <= 16;
            }

            return false;
        }

        // Ищем Java 8 в типичных местах: Oracle/Adoptium в Program Files,
        // а также Oracle java8path (x86) как запасной вариант.
        public static string? FindLegacyJava()
        {
            try
            {
                string[] roots =
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java")
                };

                string? best = null;

                foreach (string root in roots)
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(dir).ToLowerInvariant();

                        bool looksLikeJava8 =
                            name.StartsWith("jre1.8") ||
                            name.StartsWith("jdk1.8") ||
                            name.StartsWith("jre8") ||
                            name.StartsWith("jdk8") ||
                            name.Contains("temurin-8") ||
                            name.Contains("jre-legacy");

                        if (!looksLikeJava8)
                        {
                            continue;
                        }

                        string exe = Path.Combine(dir, "bin", "java.exe");

                        if (File.Exists(exe) &&
                            (best == null || string.CompareOrdinal(exe, best) > 0))
                        {
                            best = exe;
                        }
                    }
                }

                if (best != null)
                {
                    return best;
                }

                // Oracle java8path (x86) — обычно 32-битная Java 8;
                // подойдёт, если ничего лучше не нашли (до ~1.5 ГБ ОЗУ).
                string x86 = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Common Files", "Oracle", "Java", "java8path", "java.exe");

                if (File.Exists(x86))
                {
                    return x86;
                }
            }
            catch
            {
                // не смогли поискать — не критично, будет java из PATH
            }

            return null;
        }
    }
}
