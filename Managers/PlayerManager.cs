using MCServerManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MCServerManager.Managers
{
    public class PlayerManager
    {

        public List<PlayerInfo> Players { get; set; } = new();

        // Забаненные игроки (читается напрямую из banned-players.json)
        public List<string> Banned { get; set; } = new();

        // Операторы (читается напрямую из ops.json)
        public List<string> Operators { get; set; } = new();

        // Забаненные IP-адреса (читается напрямую из banned-ips.json)
        public List<string> BannedIps { get; set; } = new();



        private ConsoleManager console;

        // Путь к папке сервера — нужен для прямой работы с
        // banned-players.json / ops.json / banned-ips.json
        private string serverPath;

        // Последний известный IP каждого игрока (по нику, без учёта регистра) —
        // собираем из строк консоли вида "Имя[/1.2.3.4:port] logged in ..."
        private readonly Dictionary<string, string> knownIps =
            new(StringComparer.OrdinalIgnoreCase);



        // Событие: список игроков онлайн реально обновился
        public event Action? PlayersUpdated;

        // Событие: список забаненных реально обновился
        public event Action? BannedUpdated;

        // Событие: список операторов реально обновился
        public event Action? OperatorsUpdated;

        // Событие: список забаненных IP реально обновился
        public event Action? BannedIpsUpdated;



        public PlayerManager(ConsoleManager consoleManager, string serverPath)
        {
            console = consoleManager;

            this.serverPath = serverPath;


            console.ConsoleReceived +=
                ConsoleReceived;
        }



        private static readonly Regex JoinIpRegex =
            new Regex(@"^(\S+)\[/([\d.]+):\d+\] logged in", RegexOptions.Compiled);



        private void ConsoleReceived(string message)
        {

            // Оставляем фильтр и здесь тоже — на случай прямого вызова UpdatePlayers снаружи
            UpdatePlayers(message);



            // Запоминаем IP игрока при заходе — понадобится для бана по IP
            Match joinMatch = JoinIpRegex.Match(message);

            if (joinMatch.Success)
            {

                knownIps[joinMatch.Groups[1].Value] = joinMatch.Groups[2].Value;

            }



            // Реагируем на подтверждение от сервера, что бан/разбан реально
            // применился — читаем файл ТОЛЬКО сейчас, а не сразу после
            // отправки команды, чтобы не словить файл до того, как сервер
            // успел его переписать
            if (message.Contains("Banned ") || message.Contains("Unbanned "))
            {

                RefreshBanned();

            }

        }








        public void UpdatePlayers(string message)
        {

            // ГЛАВНЫЙ ФИКС (из прошлого раза):
            // раньше Players.Clear() вызывался для ЛЮБОЙ строки консоли,
            // из-за чего список игроков (и выделение в ListBox) сбрасывался
            // почти на каждый тик сервера, и кнопки Кик/Бан/Оп не успевали
            // сработать по правильному игроку.
            if (!message.Contains("There are") ||
                !message.Contains("players online"))
            {
                return;
            }



            Players.Clear();



            int online =
                message.IndexOf("players online");



            if (online == -1)
                return;



            string result =
                message.Substring(
                    online
                );



            int colon =
                result.LastIndexOf(':');



            if (colon == -1)
                return;




            string players =
                result.Substring(
                    colon + 1
                )
                .Trim();





            // если игроков нет

            if (string.IsNullOrWhiteSpace(players))
            {
                PlayersUpdated?.Invoke();
                return;
            }







            string[] names =
                players.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries
                );





            foreach (string name in names)
            {

                string clean =
                    name.Trim();




                if (clean.Length > 0)
                {

                    Players.Add(
                        new PlayerInfo
                        {
                            Name = clean,
                            PlayTime = "Онлайн"
                        });

                }

            }



            // Сообщаем подписчикам (UI), что список реально изменился
            PlayersUpdated?.Invoke();

        }








        public void Refresh()
        {

            console.SendCommand(
                "list"
            );

        }








        // Кик работает по живому подключению игрока — сервер уже знает,
        // кто он, никакого обращения к Mojang тут нет, поэтому обычная
        // команда безопасна и ничего чинить не нужно.
        public void Kick(string player)
        {

            console.SendCommand(
                $"kick {player}"
            );

        }








        // ГЛАВНЫЙ ФИКС #2: простой правки banned-players.json оказалось
        // недостаточно — у сервера НЕТ команды типа "banlist reload", он
        // держит список забаненных в памяти и сверяется именно с ней, а не
        // лезет в файл заново при каждом заходе. Файл поменялся — а
        // работающий сервер об этом не узнаёт до перезапуска, поэтому
        // забаненный мог зайти обратно.
        //
        // Раз кнопка "Бан" всегда применяется к игроку из списка "Игроки
        // онлайн" — то есть игрок СЕЙЧАС подключён — можно спокойно
        // использовать настоящую команду "ban": сервер резолвит профиль по
        // уже открытой сессии этого игрока, а не лезет к Mojang вслепую
        // (тот риск был актуален только для whitelist, где игрок ещё даже
        // не подключался). Команда сама обновит И память, И файл, И кикнет.
        public void Ban(string player)
        {

            bool isOnline =
                Players.Any(p => p.Name.Equals(player, StringComparison.OrdinalIgnoreCase));



            if (isOnline && console.IsRunning)
            {

                console.SendCommand(
                    $"ban {player} Забанен администратором"
                );



                // ответ сервера подхватится в ConsoleReceived и обновит
                // список автоматически — здесь ничего досрочно не читаем,
                // чтобы не словить файл до того, как сервер успеет его
                // переписать
                return;

            }



            // Игрок не в сети (в текущем UI такое не должно происходить,
            // т.к. выбор всегда из списка "Игроки онлайн", но оставляем
            // как запасной вариант) — правим файл напрямую. Учти: пока
            // сервер не перезапустят, эффект не гарантирован.
            string path =
                Path.Combine(serverPath, "banned-players.json");



            List<BanEntry> entries =
                ReadJsonFile<BanEntry>(path);



            entries.RemoveAll(
                e => e.Name.Equals(player, StringComparison.OrdinalIgnoreCase)
            );



            entries.Add(new BanEntry
            {
                Uuid = OfflineUuid.Compute(player),
                Name = player,
                Created = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss zz00"),
                Source = "Server",
                Expires = "forever",
                Reason = "Banned by an operator."
            });



            WriteJsonFile(path, entries);



            RefreshBanned();

        }




        // Отдельного игрока баним, только пока он online — команда "pardon"
        // безопасна, т.к. этот игрок уже когда-то подключался к серверу и
        // его профиль уже есть в локальном usercache.json, так что сервер
        // найдёт его точный UUID локально, не обращаясь к Mojang.
        public void Unban(string player)
        {

            if (console.IsRunning)
            {

                console.SendCommand(
                    $"pardon {player}"
                );

            }



            // Дополнительно правим файл напрямую — гарантирует, что даже
            // если live-команда выше почему-то не сработает (например,
            // сервер выключен), запись всё равно уберётся к следующему
            // запуску сервера.
            string path =
                Path.Combine(serverPath, "banned-players.json");



            List<BanEntry> entries =
                ReadJsonFile<BanEntry>(path);



            entries.RemoveAll(
                e => e.Name.Equals(player, StringComparison.OrdinalIgnoreCase)
            );



            WriteJsonFile(path, entries);



            RefreshBanned();

        }








        public void RefreshBanned()
        {

            string path =
                Path.Combine(serverPath, "banned-players.json");



            List<BanEntry> entries =
                ReadJsonFile<BanEntry>(path);



            Banned.Clear();



            foreach (var entry in entries)
            {
                Banned.Add(entry.Name);
            }



            BannedUpdated?.Invoke();

        }








        // ГЛАВНЫЙ ФИКС: та же логика, что и для бана — правим ops.json
        // напрямую с offline-UUID. Если игрок online — дополнительно шлём
        // живую команду "op", чтобы права применились к уже открытой
        // сессии немедленно (иначе подействует только при следующем входе).
        public void GiveOperator(string player)
        {

            string path =
                Path.Combine(serverPath, "ops.json");



            List<OpEntry> entries =
                ReadJsonFile<OpEntry>(path);



            entries.RemoveAll(
                e => e.Name.Equals(player, StringComparison.OrdinalIgnoreCase)
            );



            entries.Add(new OpEntry
            {
                Uuid = OfflineUuid.Compute(player),
                Name = player,
                Level = 4,
                BypassesPlayerLimit = false
            });



            WriteJsonFile(path, entries);



            bool isOnline =
                Players.Any(p => p.Name.Equals(player, StringComparison.OrdinalIgnoreCase));



            if (isOnline && console.IsRunning)
            {

                // применяем права к уже открытой сессии немедленно
                console.SendCommand($"op {player}");

            }



            RefreshOperators();

        }








        public void RemoveOperator(string player)
        {

            string path =
                Path.Combine(serverPath, "ops.json");



            List<OpEntry> entries =
                ReadJsonFile<OpEntry>(path);



            entries.RemoveAll(
                e => e.Name.Equals(player, StringComparison.OrdinalIgnoreCase)
            );



            WriteJsonFile(path, entries);



            bool isOnline =
                Players.Any(p => p.Name.Equals(player, StringComparison.OrdinalIgnoreCase));



            if (isOnline && console.IsRunning)
            {

                console.SendCommand($"deop {player}");

            }



            RefreshOperators();

        }








        public void RefreshOperators()
        {

            string path =
                Path.Combine(serverPath, "ops.json");



            List<OpEntry> entries =
                ReadJsonFile<OpEntry>(path);



            Operators.Clear();



            foreach (var entry in entries)
            {
                Operators.Add(entry.Name);
            }



            OperatorsUpdated?.Invoke();

        }




        // ===================== БАН ПО IP =====================


        // Возвращает последний известный IP игрока по нику (если он хотя
        // бы раз заходил в текущей сессии консоли), иначе null
        public string? GetKnownIp(string playerName)
        {

            return knownIps.TryGetValue(playerName, out string? ip) ? ip : null;

        }




        // Бан по IP — самый надёжный способ не дать зайти с другого
        // аккаунта, т.к. новый ник/UUID тут вообще не имеет значения.
        // Команда "ban-ip" не требует резолва профиля через Mojang (в
        // отличие от обычного "ban"), поэтому спокойно шлём её напрямую.
        public void BanIp(string ip)
        {

            if (console.IsRunning)
            {

                console.SendCommand($"ban-ip {ip} Забанен администратором");

            }
            else
            {

                // сервер выключен — правим файл напрямую, применится при
                // следующем запуске
                string path =
                    Path.Combine(serverPath, "banned-ips.json");



                List<BanIpEntry> entries =
                    ReadJsonFile<BanIpEntry>(path);



                entries.RemoveAll(e => e.Ip == ip);



                entries.Add(new BanIpEntry
                {
                    Ip = ip,
                    Created = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss zz00"),
                    Source = "Server",
                    Expires = "forever",
                    Reason = "Забанен администратором"
                });



                WriteJsonFile(path, entries);

            }



            RefreshBannedIps();

        }








        public void UnbanIp(string ip)
        {

            if (console.IsRunning)
            {

                console.SendCommand($"pardon-ip {ip}");

            }
            else
            {

                string path =
                    Path.Combine(serverPath, "banned-ips.json");



                List<BanIpEntry> entries =
                    ReadJsonFile<BanIpEntry>(path);



                entries.RemoveAll(e => e.Ip == ip);



                WriteJsonFile(path, entries);

            }



            RefreshBannedIps();

        }








        public void RefreshBannedIps()
        {

            string path =
                Path.Combine(serverPath, "banned-ips.json");



            List<BanIpEntry> entries =
                ReadJsonFile<BanIpEntry>(path);



            BannedIps.Clear();



            foreach (var entry in entries)
            {
                BannedIps.Add(entry.Ip);
            }



            BannedIpsUpdated?.Invoke();

        }




        // ===================== общие помощники для JSON-файлов =====================


        private List<T> ReadJsonFile<T>(string path)
        {

            if (!File.Exists(path))
            {
                return new List<T>();
            }



            try
            {

                string json =
                    File.ReadAllText(path);



                List<T>? entries =
                    JsonSerializer.Deserialize<List<T>>(json);



                return entries ?? new List<T>();

            }
            catch
            {

                return new List<T>();

            }

        }








        private void WriteJsonFile<T>(string path, List<T> entries)
        {

            string json =
                JsonSerializer.Serialize(
                    entries,
                    new JsonSerializerOptions { WriteIndented = true }
                );



            File.WriteAllText(path, json);

        }








        // ===================== форматы записей =====================


        private class BanEntry
        {

            [JsonPropertyName("uuid")]
            public string Uuid { get; set; } = "";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("created")]
            public string Created { get; set; } = "";

            [JsonPropertyName("source")]
            public string Source { get; set; } = "";

            [JsonPropertyName("expires")]
            public string Expires { get; set; } = "";

            [JsonPropertyName("reason")]
            public string Reason { get; set; } = "";

        }



        private class OpEntry
        {

            [JsonPropertyName("uuid")]
            public string Uuid { get; set; } = "";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("level")]
            public int Level { get; set; } = 4;

            [JsonPropertyName("bypassesPlayerLimit")]
            public bool BypassesPlayerLimit { get; set; } = false;

        }




        private class BanIpEntry
        {

            [JsonPropertyName("ip")]
            public string Ip { get; set; } = "";

            [JsonPropertyName("created")]
            public string Created { get; set; } = "";

            [JsonPropertyName("source")]
            public string Source { get; set; } = "";

            [JsonPropertyName("expires")]
            public string Expires { get; set; } = "";

            [JsonPropertyName("reason")]
            public string Reason { get; set; } = "";

        }

    }
}