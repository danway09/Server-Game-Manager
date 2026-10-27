using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MCServerManager.Managers
{
    // SteamCMD (и, соответственно, tModLoader, который через него ставится)
    // категорически отказывается работать, если ГДЕ-ЛИБО в полном пути
    // встречаются не-английские символы — включая кириллицу в имени
    // пользователя Windows (C:\Users\Артём\...). Перенести проблему некуда,
    // если оставаться внутри профиля пользователя — поэтому для
    // tModLoader-серверов используем отдельный фиксированный путь на C:\,
    // который гарантированно состоит только из ASCII-символов, независимо
    // от того, как называется пользователь или где лежит сам проект.
    public static class TModLoaderPaths
    {

        private const string SafeRoot = @"C:\MCServerManagerData";



        public static string SteamCmdFolder =>
            Path.Combine(SafeRoot, "SteamCMD");



        // Уникальная безопасная папка под конкретный сервер — по имени
        // сервера (только ASCII-буквы/цифры) + короткий хэш, чтобы два
        // сервера с похожими/кириллическими именами не схлопнулись в одну
        // и ту же папку
        public static string GetServerInstallDir(string serverName)
        {

            string safeName =
                new string(
                    serverName
                        .Where(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')
                        .ToArray()
                );



            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "server";
            }



            using MD5 md5 = MD5.Create();



            byte[] hash =
                md5.ComputeHash(Encoding.UTF8.GetBytes(serverName));



            string shortHash =
                BitConverter.ToString(hash).Replace("-", "").Substring(0, 6).ToLowerInvariant();



            return Path.Combine(SafeRoot, "TModLoaderServers", safeName + "_" + shortHash);

        }

    }
}