using System;

namespace MCServerManager.Models
{
    public class MinecraftServer
    {
        public string Name { get; set; } = "";


        // Папка сервера
        public string Path { get; set; } = "";



        // Ядро
        public string Core { get; set; } = "Paper";



        // Версия Minecraft
        public string Version { get; set; } = "1.21.8";



        // Оперативная память
        public int Ram { get; set; } = 2048;



        // Статус
        public string Status { get; set; } = "Остановлен";



        // Порт
        public int Port { get; set; } = 25565;



        // Пиратский сервер: true = обычный лицензионный режим Mojang
        // (проверка аккаунта), false = разрешает заходить пиратским клиентам
        public bool OnlineMode { get; set; } = true;



        // Домен (если куплен) — просто хранится для отображения в
        // информации о сервере, саму DNS-настройку нужно делать у
        // регистратора домена (направить A-запись на внешний IP)
        public string Domain { get; set; } = "";



        // Дата создания
        public DateTime Created { get; set; } = DateTime.Now;





        // запущен ли сервер

        public bool IsRunning
        {
            get
            {
                return Status == "Запущен";
            }
        }





        // включить статус

        public void Start()
        {
            Status = "Запущен";
        }





        // выключить статус

        public void Stop()
        {
            Status = "Остановлен";
        }





        // информация о сервере

        public string GetInfo()
        {
            return
                $"Название: {Name}\n" +
                $"Ядро: {Core}\n" +
                $"Версия: {Version}\n" +
                $"ОЗУ: {Ram} MB\n" +
                $"Порт: {Port}\n" +
                $"Режим: {(OnlineMode ? "Лицензионный" : "Пиратский")}\n" +
                $"Статус: {Status}\n" +
                $"Создан: {Created:dd.MM.yyyy}";
        }
    }
}