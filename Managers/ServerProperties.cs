using System;
using System.Collections.Generic;
using System.IO;

namespace MCServerManager.Managers
{
    // Общая логика чтения/записи одной строки в server.properties.
    // Раньше похожий код был отдельно скопирован в ConsoleManager
    // (online-mode) и WhitelistManager (white-list) — здесь он один раз.
    public static class ServerProperties
    {

        public static string Get(string serverPath, string key, string defaultValue)
        {

            string path =
                Path.Combine(serverPath, "server.properties");



            if (!File.Exists(path))
                return defaultValue;



            foreach (string line in File.ReadAllLines(path))
            {

                if (line.StartsWith(key + "="))
                {

                    return line.Substring(key.Length + 1).Trim();

                }

            }



            return defaultValue;

        }








        public static void Set(string serverPath, string key, string value)
        {

            string path =
                Path.Combine(serverPath, "server.properties");



            string valueLine =
                key + "=" + value;



            if (!File.Exists(path))
            {

                File.WriteAllText(path, valueLine + Environment.NewLine);


                return;

            }



            List<string> lines =
                new List<string>(File.ReadAllLines(path));



            bool found = false;



            for (int i = 0; i < lines.Count; i++)
            {

                if (lines[i].StartsWith(key + "="))
                {

                    lines[i] = valueLine;


                    found = true;


                    break;

                }

            }



            if (found)
            {

                File.WriteAllLines(path, lines);

            }
            else
            {

                lines.Add(valueLine);


                File.WriteAllLines(path, lines);

            }

        }

    }
}