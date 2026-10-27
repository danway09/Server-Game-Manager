using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerManager.Managers
{
    // Подключается к NetworkHost на компьютере друга и управляет его
    // сервером по сети — те же действия, что доступны локально, просто
    // уходят по TCP вместо прямого вызова ConsoleManager.
    public class NetworkClient : IDisposable
    {

        private TcpClient? client;

        private StreamWriter? writer;

        private StreamReader? reader;

        private CancellationTokenSource? cts;



        public bool IsConnected { get; private set; }

        public string ServerName { get; private set; } = "";



        public event Action<string>? ConsoleReceived;

        public event Action<List<string>>? PlayersReceived;

        public event Action<List<string>>? WhitelistReceived;

        public event Action<List<string>>? BannedReceived;

        public event Action<bool>? StatusReceived;

        public event Action? Disconnected;








        // Возвращает true при успешном подключении+авторизации, false —
        // если пароль неверный или хост недоступен
        public async Task<bool> ConnectAsync(string host, int port, string password)
        {

            try
            {

                client = new TcpClient();



                await client.ConnectAsync(host, port);



                NetworkStream stream =
                    client.GetStream();



                reader =
                    new StreamReader(stream, Encoding.UTF8);



                writer =
                    new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };



                await writer.WriteLineAsync(
                    JsonSerializer.Serialize(new { type = "auth", text = password })
                );



                string? response =
                    await reader.ReadLineAsync();



                if (response == null)
                    return false;



                using JsonDocument doc =
                    JsonDocument.Parse(response);



                string type =
                    doc.RootElement.GetProperty("type").GetString() ?? "";



                if (type != "auth_ok")
                {

                    client.Close();


                    return false;

                }



                if (doc.RootElement.TryGetProperty("serverName", out JsonElement nameEl))
                {
                    ServerName = nameEl.GetString() ?? "";
                }



                IsConnected = true;



                cts = new CancellationTokenSource();



                _ = ReceiveLoopAsync(cts.Token);



                return true;

            }
            catch
            {

                return false;

            }

        }








        private async Task ReceiveLoopAsync(CancellationToken token)
        {

            try
            {

                while (!token.IsCancellationRequested)
                {

                    string? line =
                        await reader!.ReadLineAsync();



                    if (line == null)
                        break;



                    HandleMessage(line);

                }

            }
            catch
            {
                // соединение оборвалось
            }
            finally
            {

                IsConnected = false;


                Disconnected?.Invoke();

            }

        }








        private void HandleMessage(string json)
        {

            try
            {

                using JsonDocument doc =
                    JsonDocument.Parse(json);



                string type =
                    doc.RootElement.GetProperty("type").GetString() ?? "";



                switch (type)
                {

                    case "console":

                        ConsoleReceived?.Invoke(
                            doc.RootElement.GetProperty("text").GetString() ?? ""
                        );

                        break;



                    case "players":

                        PlayersReceived?.Invoke(ReadList(doc));

                        break;



                    case "whitelist":

                        WhitelistReceived?.Invoke(ReadList(doc));

                        break;



                    case "banned":

                        BannedReceived?.Invoke(ReadList(doc));

                        break;



                    case "status":

                        StatusReceived?.Invoke(
                            doc.RootElement.GetProperty("running").GetBoolean()
                        );

                        break;

                }

            }
            catch
            {
                // битое сообщение от хоста — игнорируем
            }

        }








        private List<string> ReadList(JsonDocument doc)
        {

            List<string> list = new();



            foreach (JsonElement item in doc.RootElement.GetProperty("list").EnumerateArray())
            {

                list.Add(item.GetString() ?? "");

            }



            return list;

        }








        // ===================== команды на хост =====================


        public void SendCommand(string command) => Send("command", command);

        public void Start() => Send("start");

        public void Stop() => Send("stop");

        public void Kick(string player) => Send("kick", player);

        public void Ban(string player) => Send("ban", player);

        public void Unban(string player) => Send("unban", player);

        public void Op(string player) => Send("op", player);

        public void Deop(string player) => Send("deop", player);

        public void WhitelistAdd(string player) => Send("whitelist_add", player);

        public void WhitelistRemove(string player) => Send("whitelist_remove", player);

        public void WhitelistEnable() => Send("whitelist_enable");

        public void WhitelistDisable() => Send("whitelist_disable");

        public void RefreshPlayers() => Send("refresh_players");

        public void RefreshBanned() => Send("refresh_banned");

        public void RefreshWhitelist() => Send("refresh_whitelist");








        private void Send(string type, string? text = null)
        {

            if (writer == null || !IsConnected)
                return;



            try
            {

                writer.WriteLine(
                    JsonSerializer.Serialize(new { type, text })
                );

            }
            catch
            {
                // соединение могло только что оборваться — не критично
            }

        }








        public void Dispose()
        {

            cts?.Cancel();



            try { client?.Close(); } catch { }

        }

    }
}