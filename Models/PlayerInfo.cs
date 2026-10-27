namespace MCServerManager.Models
{
    public class PlayerInfo
    {
        public string Name { get; set; } = "";

        public string UUID { get; set; } = "";

        public string IP { get; set; } = "";

        public string Position { get; set; } = "X:0 Y:0 Z:0";

        public string PlayTime { get; set; } = "0 минут";

        public bool IsOperator { get; set; }


        public string GetInfo()
        {
            return
                $"Игрок: {Name}\n" +
                $"UUID: {UUID}\n" +
                $"IP: {IP}\n" +
                $"Координаты: {Position}\n" +
                $"Время игры: {PlayTime}\n" +
                $"OP: {(IsOperator ? "Да" : "Нет")}";
        }
    }
}