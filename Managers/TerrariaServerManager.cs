using MCServerManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MCServerManager.Managers
{
    public class TerrariaServerManager
    {

        private readonly string serversPath = "TerrariaServers";


        public List<TerrariaServer> Servers { get; set; } = new();





        public TerrariaServerManager()
        {

            if (!Directory.Exists(serversPath))
            {
                Directory.CreateDirectory(serversPath);
            }


            LoadServers();

        }








        public bool CreateServer(TerrariaServer server)
        {

            string path =
                Path.Combine(serversPath, server.Name);



            if (Directory.Exists(path))
            {
                return false;
            }



            Directory.CreateDirectory(path);



            server.Path = path;

            server.Status = "Остановлен";



            SaveConfig(server);



            Servers.Add(server);



            return true;

        }








        public void SaveConfig(TerrariaServer server)
        {

            if (string.IsNullOrEmpty(server.Path))
                return;



            string file =
                Path.Combine(server.Path, "server.config");



            string json =
                JsonSerializer.Serialize(
                    server,
                    new JsonSerializerOptions { WriteIndented = true }
                );



            File.WriteAllText(file, json);

        }








        public void LoadServers()
        {

            Servers.Clear();



            if (!Directory.Exists(serversPath))
                return;



            foreach (string folder in Directory.GetDirectories(serversPath))
            {

                string config =
                    Path.Combine(folder, "server.config");



                if (!File.Exists(config))
                    continue;



                try
                {

                    string json =
                        File.ReadAllText(config);



                    TerrariaServer? server =
                        JsonSerializer.Deserialize<TerrariaServer>(json);



                    if (server != null)
                    {

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








        public void DeleteServer(TerrariaServer server)
        {

            FileHelper.TryDeleteDirectoryWithRetries(server.Path);



            Servers.Remove(server);

        }








        public bool RenameServer(TerrariaServer server, string newName)
        {

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








        public TerrariaServer? GetServer(string name)
        {

            return Servers.Find(x => x.Name == name);

        }

    }
}