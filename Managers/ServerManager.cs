using MCServerManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MCServerManager.Managers
{
    public class ServerManager
    {

        private readonly string serversPath = "Servers";


        public List<MinecraftServer> Servers { get; set; } = new();





        public ServerManager()
        {

            if (!Directory.Exists(serversPath))
            {
                Directory.CreateDirectory(serversPath);
            }


            LoadServers();

        }








        // создание сервера

        public bool CreateServer(MinecraftServer server)
        {

            // Имя сервера = имя папки: обрезаем пробелы по краям — Windows
            // такие имена потом не открывает (такая битая папка уже была).
            server.Name = server.Name.Trim();

            string path =
                Path.Combine(
                    serversPath,
                    server.Name
                );



            if (Directory.Exists(path))
            {
                return false;
            }





            Directory.CreateDirectory(path);


            Directory.CreateDirectory(
                Path.Combine(path, "plugins")
            );


            Directory.CreateDirectory(
                Path.Combine(path, "world")
            );


            Directory.CreateDirectory(
                Path.Combine(path, "logs")
            );


            Directory.CreateDirectory(
                Path.Combine(path, "backups")
            );





            server.Path = path;


            server.Status =
                "Остановлен";





            // EULA

            File.WriteAllText(
                Path.Combine(path, "eula.txt"),
                "eula=true"
            );







            // server.properties
            // online-mode теперь берётся из server.OnlineMode (задаётся
            // чекбоксом "Пиратский сервер" при создании), а не зашит намертво

            string onlineModeValue =
                server.OnlineMode ? "true" : "false";



            File.WriteAllText(
                Path.Combine(path, "server.properties"),

$@"motd={server.Name}
online-mode={onlineModeValue}
spawn-protection=0
max-players=20
server-port={server.Port}
difficulty=easy
gamemode=survival
view-distance=10
simulation-distance=10");







            SaveConfig(server);



            Servers.Add(server);



            return true;

        }

        public void SaveConfig(MinecraftServer server)
        {

            if (string.IsNullOrEmpty(server.Path))
                return;



            string file =
                Path.Combine(
                    server.Path,
                    "server.config"
                );




            string json =
                JsonSerializer.Serialize(
                    server,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });



            File.WriteAllText(
                file,
                json
            );

        }




        // переименование сервера — физически переименовывает папку
        // и обновляет всё, что от неё зависит
        public bool RenameServer(MinecraftServer server, string newName)
        {

            newName = newName.Trim();

            if (string.IsNullOrWhiteSpace(newName))
                return false;



            if (newName == server.Name)
                return true;



            string newPath =
                Path.Combine(serversPath, newName);



            if (Directory.Exists(newPath))
                return false;



            Directory.Move(server.Path, newPath);



            server.Name = newName;

            server.Path = newPath;



            SaveConfig(server);



            return true;

        }








        // загрузка серверов

        public void LoadServers()
        {

            Servers.Clear();



            if (!Directory.Exists(serversPath))
                return;





            foreach (string folder in Directory.GetDirectories(serversPath))
            {

                string config =
                    Path.Combine(
                        folder,
                        "server.config"
                    );



                if (!File.Exists(config))
                    continue;





                try
                {

                    string json =
                        File.ReadAllText(config);



                    MinecraftServer? server =
                        JsonSerializer.Deserialize<MinecraftServer>(
                            json
                        );



                    if (server != null)
                    {

                        // если путь потерялся

                        if (string.IsNullOrEmpty(server.Path))
                        {
                            server.Path = folder;
                        }



                        Servers.Add(server);

                    }


                }
                catch
                {

                }

            }

        }

        public void DeleteServer(MinecraftServer server)
        {

            FileHelper.TryDeleteDirectoryWithRetries(server.Path);



            Servers.Remove(server);

        }








        // получить сервер

        public MinecraftServer? GetServer(string name)
        {

            return Servers.Find(
                x => x.Name == name
            );

        }








        // сохранить все сервера

        public void SaveAll()
        {

            foreach (var server in Servers)
            {
                SaveConfig(server);
            }

        }

    }
}