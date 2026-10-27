using MCServerManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // Поднимает TCP-сервис на этом ПК, чтобы друг мог подключиться из своей
    // копии программы и управлять этим же сервером — с паролем и теми же
    // правами, что и у хозяина. Работает только пока это приложение
    // запущено (хозяин — "сервер управления").
    public class NetworkHost : IDisposable
    {

        private TcpListener? listener;

        private readonly List<TcpClient> clients = new();

        private readonly object clientsLock = new();



        private string password = "";



        private readonly ConsoleManager consoleManager;

        private readonly PlayerManager playerManager;

        private readonly WhitelistManager whitelistManager;

        private readonly MinecraftServer server;



        private CancellationTokenSource? cts;



        public bool IsHosting { get; private set; }

        public int ConnectedClients
        {
            get
            {
                lock (clientsLock)
                {
                    return clients.Count;
                }
            }
        }



        public NetworkHost(
            ConsoleManager consoleManager,
            PlayerManager playerManager,
            WhitelistManager whitelistManager,
            MinecraftServer server)
        {

            this.consoleManager = consoleManager;

            this.playerManager = playerManager;

            this.whitelistManager = whitelistManager;

            this.server = server;



            consoleManager.ConsoleReceived +=
                text => Broadcast(new { type = "console", text });



            playerManager.PlayersUpdated +=
                () => Broadcast(new
                {
                    type = "players",
                    list = playerManager.Players.Select(p => p.Name).ToList()
                });



            playerManager.BannedUpdated +=
                () => Broadcast(new { type = "banned", list = playerManager.Banned });



            playerManager.OperatorsUpdated +=
                () => Broadcast(new { type = "operators", list = playerManager.Operators });



            whitelistManager.WhitelistUpdated +=
                () => Broadcast(new { type = "whitelist", list = whitelistManager.Players });

        }








        public void StartHosting(int port, string hostPassword)
        {

            if (IsHosting)
                return;



            password = hostPassword;



            listener =
                new TcpListener(IPAddress.Any, port);


            listener.Start();



            IsHosting = true;



            cts = new CancellationTokenSource();



            _ = AcceptLoopAsync(cts.Token);

        }








        public void StopHosting()
        {

            IsHosting = false;



            cts?.Cancel();



            try { listener?.Stop(); } catch { }



            lock (clientsLock)
            {

                foreach (var client in clients)
                {
                    try { client.Close(); } catch { }
                }



                clients.Clear();

            }

        }








        private async Task AcceptLoopAsync(CancellationToken token)
        {

            try
            {

                while (!token.IsCancellationRequested)
                {

                    TcpClient client =
                        await listener!.AcceptTcpClientAsync();



                    _ = HandleClientAsync(client, token);

                }

            }
            catch
            {
                // слушатель остановлен — нормальное завершение
            }

        }








        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {

            try
            {

                NetworkStream stream =
                    client.GetStream();



                StreamReader reader =
                    new StreamReader(stream, Encoding.UTF8);



                StreamWriter writer =
                    new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };



                string? authLine =
                    await reader.ReadLineAsync();



                NetMessage? authMsg =
                    authLine != null
                        ? JsonSerializer.Deserialize<NetMessage>(authLine)
                        : null;



                if (authMsg?.Type != "auth" || authMsg.Text != password)
                {

                    await writer.WriteLineAsync(
                        JsonSerializer.Serialize(new { type = "auth_fail" })
                    );


                    client.Close();


                    return;

                }



                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(new { type = "auth_ok", serverName = server.Name })
                );



                lock (clientsLock)
                {
                    clients.Add(client);
                }



                // сразу отправляем текущее состояние, чтобы у друга не было
                // пустых списков до первого события
                SendTo(writer, new
                {
                    type = "players",
                    list = playerManager.Players.Select(p => p.Name).ToList()
                });


                SendTo(writer, new { type = "whitelist", list = whitelistManager.Players });


                SendTo(writer, new { type = "banned", list = playerManager.Banned });


                SendTo(writer, new { type = "status", running = consoleManager.IsRunning });



                while (!token.IsCancellationRequested)
                {

                    string? line =
                        await reader.ReadLineAsync();



                    if (line == null)
                        break;



                    HandleClientMessage(line);

                }

            }
            catch
            {
                // клиент отвалился — не критично
            }
            finally
            {

                lock (clientsLock)
                {
                    clients.Remove(client);
                }



                try { client.Close(); } catch { }

            }

        }








        private void HandleClientMessage(string json)
        {

            try
            {

                NetMessage? msg =
                    JsonSerializer.Deserialize<NetMessage>(json);



                if (msg == null)
                    return;



                switch (msg.Type)
                {

                    case "start":

                        consoleManager.StartServer(
                            server.Path, server.Ram, server.OnlineMode, server.Core, server.Version
                        );

                        break;



                    case "stop":

                        consoleManager.StopServer();

                        break;



                    case "command":

                        consoleManager.SendCommand(msg.Text ?? "");

                        break;



                    case "kick":

                        playerManager.Kick(msg.Text ?? "");

                        break;



                    case "ban":

                        playerManager.Ban(msg.Text ?? "");

                        break;



                    case "unban":

                        playerManager.Unban(msg.Text ?? "");

                        break;



                    case "op":

                        playerManager.GiveOperator(msg.Text ?? "");

                        break;



                    case "deop":

                        playerManager.RemoveOperator(msg.Text ?? "");

                        break;



                    case "whitelist_add":

                        whitelistManager.Add(msg.Text ?? "");

                        break;



                    case "whitelist_remove":

                        whitelistManager.Remove(msg.Text ?? "");

                        break;



                    case "whitelist_enable":

                        whitelistManager.Enable();

                        break;



                    case "whitelist_disable":

                        whitelistManager.Disable();

                        break;



                    case "refresh_players":

                        playerManager.Refresh();

                        break;



                    case "refresh_banned":

                        playerManager.RefreshBanned();

                        break;



                    case "refresh_whitelist":

                        whitelistManager.Refresh();

                        break;

                }

            }
            catch
            {
                // битое сообщение от клиента — просто игнорируем
            }

        }








        private void SendTo(StreamWriter writer, object payload)
        {

            try
            {

                writer.WriteLine(JsonSerializer.Serialize(payload));

            }
            catch
            {
                // клиент мог отвалиться прямо в этот момент — не критично
            }

        }








        private void Broadcast(object payload)
        {

            string json =
                JsonSerializer.Serialize(payload);



            lock (clientsLock)
            {

                foreach (TcpClient client in clients.ToList())
                {

                    try
                    {

                        StreamWriter writer =
                            new StreamWriter(client.GetStream(), Encoding.UTF8)
                            {
                                AutoFlush = true
                            };



                        writer.WriteLine(json);

                    }
                    catch
                    {

                        clients.Remove(client);

                    }

                }

            }

        }








        public void Dispose()
        {

            StopHosting();

        }








        private class NetMessage
        {

            [JsonPropertyName("type")]
            public string? Type { get; set; }



            [JsonPropertyName("text")]
            public string? Text { get; set; }

        }

    }
}