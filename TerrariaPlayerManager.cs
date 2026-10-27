using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MCServerManager.Managers
{
    // У TShock нет UUID/авторизации Mojang, как у Minecraft — тут всё
    // намного проще: кик/бан просто по нику через обычные консольные
    // команды TShock, без риска словить чужой профиль.
    public class TerrariaPlayerManager
    {

        public List<string> Players { get; } = new();



        public event Action? PlayersUpdated;



        private readonly TerrariaConsoleManager console;



        // ГЛАВНЫЙ ФИКС: раньше регулярки требовали, чтобы ВСЯ строка
        // совпадала целиком (^...$) — если TShock добавляет что-то перед
        // именем (таймстамп, префикс уровня лога и т.п.), совпадение не
        // происходило и игрок не появлялся в списке. Теперь просто ищем
        // "X has joined"/"has left" где угодно в строке.
        private static readonly Regex JoinRegex =
            new(@"(\S+) has joined\.?", RegexOptions.Compiled);



        private static readonly Regex LeaveRegex =
            new(@"(\S+) has left\.?", RegexOptions.Compiled);



        public TerrariaPlayerManager(TerrariaConsoleManager consoleManager)
        {

            console = consoleManager;



            console.ConsoleReceived += OnConsoleReceived;

        }








        private void OnConsoleReceived(string message)
        {

            string trimmed = message.Trim();



            Match joinMatch = JoinRegex.Match(trimmed);



            if (joinMatch.Success)
            {

                string name = joinMatch.Groups[1].Value.Trim();



                if (!Players.Exists(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {

                    Players.Add(name);


                    PlayersUpdated?.Invoke();

                }



                return;

            }



            Match leaveMatch = LeaveRegex.Match(trimmed);



            if (leaveMatch.Success)
            {

                string name = leaveMatch.Groups[1].Value.Trim();



                int removed =
                    Players.RemoveAll(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));



                if (removed > 0)
                {
                    PlayersUpdated?.Invoke();
                }

            }

        }








        public void Kick(string player, string reason = "Kicked by admin")
        {

            console.SendCommand($"kick {player} {reason}");

        }








        public void Ban(string player, string reason = "Banned by admin")
        {

            console.SendCommand($"ban add {player} {reason}");



            Players.RemoveAll(p => p.Equals(player, StringComparison.OrdinalIgnoreCase));


            PlayersUpdated?.Invoke();

        }








        public void Clear()
        {

            Players.Clear();


            PlayersUpdated?.Invoke();

        }

    }
}