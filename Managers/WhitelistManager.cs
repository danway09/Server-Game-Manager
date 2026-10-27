using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCServerManager.Managers
{
    public class WhitelistManager
    {

        public List<string> Players { get; set; } = new();



        private ConsoleManager console;

        // Путь к папке сервера — нужен, чтобы редактировать whitelist.json
        // и server.properties напрямую
        private string serverPath;



        // Событие: список реально обновился — только тогда перерисовываем UI
        public event Action? WhitelistUpdated;



        public WhitelistManager(ConsoleManager consoleManager, string serverPath)
        {
            console = consoleManager;

            this.serverPath = serverPath;


            console.ConsoleReceived +=
                ConsoleReceived;
        }








        // Оставлено для случая, если кто-то вручную напишет "whitelist list"
        // в консоль команд — просто подхватит ответ сервера. НЕ является
        // основным источником данных для UI (см. RefreshFromFile).
        private void ConsoleReceived(string message)
        {

            if (!message.Contains("whitelisted player"))
                return;



            RefreshFromFile();

        }








        // ГЛАВНЫЙ ИСТОЧНИК ПРАВДЫ: читаем whitelist.json напрямую с диска.
        // Работает независимо от того, запущен сервер или нет — раньше
        // список был виден только пока сервер работал и успевал ответить
        // на команду "list", теперь же он всегда актуален.
        public void RefreshFromFile()
        {

            string path =
                Path.Combine(serverPath, "whitelist.json");



            List<WhitelistEntry> entries =
                ReadWhitelistFile(path);



            Players.Clear();



            foreach (var entry in entries)
            {
                Players.Add(entry.Name);
            }



            WhitelistUpdated?.Invoke();

        }








        // Совместимость со старым названием метода
        public void Refresh()
        {

            RefreshFromFile();

        }








        // ГЛАВНЫЙ ФИКС: вместо команды "whitelist add <ник>" (которая на
        // offline-mode сервере всё равно обращается к Mojang API и может
        // подставить ЧУЖОЙ UUID и другой регистр имени) — редактируем
        // whitelist.json НАПРЯМУЮ, вычисляя UUID точно так же, как это
        // делает сам offline-режим клиента: UUID.nameUUIDFromBytes(
        // "OfflinePlayer:<ник>"). Так UUID гарантированно совпадёт с тем,
        // что сгенерирует клиент при заходе — регистр и написание должны
        // быть введены ТОЧНО как в игре.
        public void Add(string player)
        {

            string path =
                Path.Combine(serverPath, "whitelist.json");



            List<WhitelistEntry> entries =
                ReadWhitelistFile(path);



            // убираем возможный дубликат с тем же именем без учёта регистра,
            // чтобы не плодить две записи на одного и того же игрока
            entries.RemoveAll(
                e => e.Name.Equals(player, StringComparison.OrdinalIgnoreCase)
            );



            entries.Add(new WhitelistEntry
            {
                Uuid = OfflineUuid.Compute(player),
                Name = player
            });



            WriteWhitelistFile(path, entries);



            // если сервер запущен — просим его перечитать файл, чтобы
            // изменение подхватилось без перезапуска
            if (console.IsRunning)
            {
                console.SendCommand("whitelist reload");
            }



            RefreshFromFile();

        }








        public void Remove(string player)
        {

            string path =
                Path.Combine(serverPath, "whitelist.json");



            List<WhitelistEntry> entries =
                ReadWhitelistFile(path);



            entries.RemoveAll(
                e => e.Name.Equals(player, StringComparison.OrdinalIgnoreCase)
            );



            WriteWhitelistFile(path, entries);



            if (console.IsRunning)
            {
                console.SendCommand("whitelist reload");
            }



            RefreshFromFile();

        }








        public void Enable()
        {

            SetWhiteListProperty(true);



            if (console.IsRunning)
            {
                console.SendCommand("whitelist on");
            }

        }








        public void Disable()
        {

            SetWhiteListProperty(false);



            if (console.IsRunning)
            {
                console.SendCommand("whitelist off");
            }

        }








        // Прописывает white-list=true/false в server.properties, чтобы
        // настройка сохранялась даже когда сервер выключен (аналогично
        // тому, как мы уже делаем с online-mode)
        private void SetWhiteListProperty(bool enabled)
        {

            string path =
                Path.Combine(serverPath, "server.properties");



            string valueLine =
                "white-list=" + (enabled ? "true" : "false");



            if (!File.Exists(path))
            {

                File.WriteAllText(path, valueLine + Environment.NewLine);


                return;

            }



            string[] lines =
                File.ReadAllLines(path);



            bool found = false;



            for (int i = 0; i < lines.Length; i++)
            {

                if (lines[i].StartsWith("white-list="))
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

                File.AppendAllText(
                    path,
                    Environment.NewLine + valueLine + Environment.NewLine
                );

            }

        }








        private List<WhitelistEntry> ReadWhitelistFile(string path)
        {

            if (!File.Exists(path))
            {
                return new List<WhitelistEntry>();
            }



            try
            {

                string json =
                    File.ReadAllText(path);



                List<WhitelistEntry>? entries =
                    JsonSerializer.Deserialize<List<WhitelistEntry>>(json);



                return entries ?? new List<WhitelistEntry>();

            }
            catch
            {

                // повреждённый/пустой файл — считаем, что список пуст
                return new List<WhitelistEntry>();

            }

        }








        private void WriteWhitelistFile(string path, List<WhitelistEntry> entries)
        {

            string json =
                JsonSerializer.Serialize(
                    entries,
                    new JsonSerializerOptions { WriteIndented = true }
                );



            File.WriteAllText(path, json);

        }








        // Формат записи в whitelist.json у ванильного/фабрик-сервера
        private class WhitelistEntry
        {

            [JsonPropertyName("uuid")]
            public string Uuid { get; set; } = "";



            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

        }

    }
}