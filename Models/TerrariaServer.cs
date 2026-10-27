using System;

namespace MCServerManager.Models
{
    public class TerrariaServer
    {

        public string Name { get; set; } = "";


        // Папка сервера
        public string Path { get; set; } = "";



        // Игровой порт (по умолчанию у Terraria — 7777, не 25565 как у Minecraft)
        public int Port { get; set; } = 7777;



        public int MaxPlayers { get; set; } = 8;



        public string Password { get; set; } = "";



        public string Motd { get; set; } = "";



        // Имя файла мира (без расширения .wld)
        public string WorldName { get; set; } = "world";



        // 1 = маленький, 2 = средний, 3 = большой
        public int WorldSize { get; set; } = 2;



        // 0 = обычная, 1 = эксперт, 2 = мастер, 3 = путешествие
        public int Difficulty { get; set; } = 0;



        // Какой движок сервера использовать: "TShock" или "tModLoader"
        public string ServerCore { get; set; } = "TShock";



        // Версия TShock/tModLoader, с которой создан сервер (для информации)
        public string TShockVersion { get; set; } = "";



        public string Status { get; set; } = "Остановлен";



        public DateTime Created { get; set; } = DateTime.Now;





        public bool IsRunning => Status == "Запущен";



        public void Start() => Status = "Запущен";



        public void Stop() => Status = "Остановлен";





        public string GetDifficultyName()
        {

            return Difficulty switch
            {
                0 => "Обычная",
                1 => "Эксперт",
                2 => "Мастер",
                3 => "Путешествие",
                _ => "?"
            };

        }



        public string GetWorldSizeName()
        {

            return WorldSize switch
            {
                1 => "Маленький",
                2 => "Средний",
                3 => "Большой",
                _ => "?"
            };

        }




        public string GetInfo()
        {
            return
                $"Название: {Name}\n" +
                $"Порт: {Port}\n" +
                $"Макс. игроков: {MaxPlayers}\n" +
                $"Мир: {WorldName} ({GetWorldSizeName()})\n" +
                $"Сложность: {GetDifficultyName()}\n" +
                $"Движок: {ServerCore} ({TShockVersion})\n" +
                $"Статус: {Status}\n" +
                $"Создан: {Created:dd.MM.yyyy}";
        }

    }
}