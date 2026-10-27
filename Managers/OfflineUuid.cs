using System;
using System.Security.Cryptography;
using System.Text;

namespace MCServerManager.Managers
{
    // Вычисляет тот же UUID, что генерирует offline-режим клиента Minecraft
    // для ника: MD5("OfflinePlayer:<ник>") с битами версии/варианта UUID v3.
    // Используется везде, где мы редактируем whitelist.json/ops.json/
    // banned-players.json напрямую, чтобы UUID гарантированно совпадал
    // с тем, что подставит клиент при подключении.
    public static class OfflineUuid
    {

        public static string Compute(string playerName)
        {

            using MD5 md5 = MD5.Create();



            byte[] hash =
                md5.ComputeHash(
                    Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName)
                );



            hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // версия 3
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // вариант RFC4122



            string hex =
                BitConverter.ToString(hash)
                    .Replace("-", "")
                    .ToLowerInvariant();



            return
                hex.Substring(0, 8) + "-" +
                hex.Substring(8, 4) + "-" +
                hex.Substring(12, 4) + "-" +
                hex.Substring(16, 4) + "-" +
                hex.Substring(20, 12);

        }

    }
}