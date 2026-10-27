using System;
using System.IO;
using System.Windows;

namespace MCServerManager.Managers
{
    /// <summary>
    /// Переключение светлой и тёмной темы.
    /// Меняет только словарь с цветами (Themes/Light.xaml или Themes/Dark.xaml),
    /// всю логику серверов это не затрагивает.
    /// </summary>
    public static class ThemeManager
    {
        // Пути в виде pack-URI: относительный путь не резолвится, когда
        // словарь создаётся из кода, а не из XAML.
        private const string LightPath =
            "pack://application:,,,/MCServerManager;component/Themes/Light.xaml";
        private const string DarkPath =
            "pack://application:,,,/MCServerManager;component/Themes/Dark.xaml";

        public static bool IsDark { get; private set; }


        private static string SettingsFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    "MCServerManager",
                    "theme.txt");
            }
        }


        // Вызывается один раз при старте приложения
        public static void ApplySaved()
        {
            bool dark = false;

            try
            {
                if (File.Exists(SettingsFile))
                {
                    dark = File.ReadAllText(SettingsFile)
                        .Trim()
                        .Equals("dark", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // не смогли прочитать сохранённый выбор — остаёмся на светлой
            }

            Apply(dark, false);
        }


        // Переключает тему на противоположную и запоминает выбор
        public static void Toggle()
        {
            Apply(!IsDark, true);
        }


        public static void Apply(bool dark, bool save)
        {
            Application? app = Application.Current;

            if (app == null)
                return;

            try
            {
                ResourceDictionary theme = new ResourceDictionary();

                theme.Source = new Uri(
                    dark ? DarkPath : LightPath,
                    UriKind.Absolute);

                // Ищем уже подключённый словарь темы, чтобы заменить его
                // на месте, а не добавлять второй рядом.
                int replaceAt = -1;

                for (int i = 0; i < app.Resources.MergedDictionaries.Count; i++)
                {
                    string? src =
                        app.Resources.MergedDictionaries[i].Source?.OriginalString;

                    if (src == null)
                        continue;

                    if (src.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                        src.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        replaceAt = i;
                        break;
                    }
                }

                if (replaceAt >= 0)
                {
                    app.Resources.MergedDictionaries.RemoveAt(replaceAt);
                    app.Resources.MergedDictionaries.Insert(replaceAt, theme);
                }
                else
                {
                    app.Resources.MergedDictionaries.Add(theme);
                }

                IsDark = dark;
            }
            catch
            {
                // отсутствующий или битый файл темы не должен ронять программу
                return;
            }

            if (!save)
                return;

            try
            {
                string? dir = Path.GetDirectoryName(SettingsFile);

                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(SettingsFile, dark ? "dark" : "light");
            }
            catch
            {
                // не смогли сохранить выбор — не критично
            }
        }
    }
}
