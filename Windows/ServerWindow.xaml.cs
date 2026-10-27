using MCServerManager.Managers;
using MCServerManager.Models;
using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows;

namespace MCServerManager
{
    public partial class ServerWindow : Window
    {

        private MinecraftServer server;

        private ConsoleManager consoleManager;

        private PlayerManager playerManager;

        private WhitelistManager whitelistManager;



        public ServerWindow(MinecraftServer selectedServer, ConsoleManager sharedConsoleManager)
        {
            InitializeComponent();


            server = selectedServer;


            // ГЛАВНЫЙ ФИКС: раньше тут создавался НОВЫЙ ConsoleManager,
            // который ничего не знал о сервере, уже запущенном через
            // MainWindow. Из-за этого IsRunning всегда был false в этом
            // окне, даже если сервер реально работал — отсюда ложное
            // "Сервер не запущен". Теперь используем ОБЩИЙ объект.
            consoleManager =
                sharedConsoleManager;



            playerManager =
                new PlayerManager(consoleManager, server.Path);



            whitelistManager =
                new WhitelistManager(consoleManager, server.Path);



            consoleManager.ConsoleReceived +=
                ConsoleManager_ConsoleReceived;


            consoleManager.ServerReady +=
                () =>
                {

                    Dispatcher.Invoke(() =>
                    {

                        InfoStatusText.Text = "🟢 Запущен";



                        MessageBox.Show(
                            server.Name + " запущен и готов принимать подключения!",
                            "Сервер готов",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );

                    });

                };


            // ВАЖНО: подписываемся на обновление списка игроков ОТДЕЛЬНО.
            // Раньше ListBox перерисовывался на КАЖДУЮ строку консоли,
            // из-за чего выделение игрока сбрасывалось и кнопки
            // Кик/Бан/Оп не успевали сработать.
            playerManager.PlayersUpdated +=
                PlayerManager_PlayersUpdated;



            playerManager.BannedUpdated +=
                PlayerManager_BannedUpdated;



            playerManager.BannedIpsUpdated +=
                PlayerManager_BannedIpsUpdated;



            whitelistManager.WhitelistUpdated +=
                WhitelistManager_WhitelistUpdated;


            // consoleManager теперь ОБЩИЙ с MainWindow, поэтому закрытие
            // этого окна НЕ должно убивать сервер — им может продолжать
            // управлять главное окно. Полная очистка процесса при
            // закрытии всего приложения по-прежнему гарантируется
            // через Job Object в ConsoleManager.



            Title =
                "Управление: " + server.Name;



            LoadServerInfo();

            LoadWhitelistCheckboxState();

            playerManager.Refresh();

            playerManager.RefreshBanned();

            playerManager.RefreshBannedIps();

            whitelistManager.Refresh();


            InfoStatusText.Text =
                consoleManager.IsRunning ? "🟢 Запущен" : "🔴 Остановлен";


            _ = LoadPublicIpAsync();

        }








        // Читает текущее значение white-list из server.properties, чтобы
        // галочка на экране не сбрасывалась каждый раз при переоткрытии окна
        private void LoadWhitelistCheckboxState()
        {

            try
            {

                string propsPath =
                    Path.Combine(server.Path, "server.properties");



                if (!File.Exists(propsPath))
                    return;



                foreach (string line in File.ReadAllLines(propsPath))
                {

                    if (line.StartsWith("white-list="))
                    {

                        bool enabled =
                            line.Substring("white-list=".Length)
                                .Trim()
                                .Equals("true", StringComparison.OrdinalIgnoreCase);



                        WhitelistEnabledCheckBox.IsChecked = enabled;


                        break;

                    }

                }

            }
            catch
            {
                // не критично — просто оставим галочку как есть
            }

        }






        // вывод консоли

        private void ConsoleManager_ConsoleReceived(
            string message)
        {

            Dispatcher.Invoke(() =>
            {

                ConsoleOutput.Text +=
                    message + "\n";


                ConsoleOutput.ScrollToEnd();

                // UpdatePlayers больше не вызываем здесь напрямую —
                // PlayerManager сам подписан на ConsoleReceived и разберёт
                // сообщение, только если это реальный ответ на "list".
                // ListBox обновляется через PlayerManager_PlayersUpdated.

            });

        }



        // список игроков реально изменился — обновляем ListBox

        private void PlayerManager_PlayersUpdated()
        {

            Dispatcher.Invoke(() =>
            {

                RefreshPlayers();

            });

        }



        // список забаненных реально изменился — обновляем ListBox

        private void PlayerManager_BannedUpdated()
        {

            Dispatcher.Invoke(() =>
            {

                RefreshBannedListBox();

            });

        }



        // whitelist реально изменился — обновляем ListBox

        private void WhitelistManager_WhitelistUpdated()
        {

            Dispatcher.Invoke(() =>
            {

                RefreshWhitelist();

            });

        }








        private void LoadServerInfo()
        {

            ConsoleOutput.Text =
                server.GetInfo();



            InfoLocalIpBox.Text =
                $"localhost:{server.Port}";



            InfoVersionBox.Text =
                server.Version;



            InfoCoreBox.Text =
                server.Core;



            InfoDomainBox.Text =
                server.Domain;

        }




        private async Task LoadPublicIpAsync()
        {

            string ip =
                await NetworkInfo.GetPublicIpAsync();



            InfoPublicIpBox.Text =
                $"{ip}:{server.Port}";

        }




        private async void RefreshPublicIp_Click(object sender, RoutedEventArgs e)
        {

            InfoPublicIpBox.Text = "определяется...";



            await LoadPublicIpAsync();

        }




        private void SaveDomain_Click(object sender, RoutedEventArgs e)
        {

            server.Domain =
                InfoDomainBox.Text.Trim();



            // сохраняем через MainWindow недоступно напрямую отсюда —
            // но server это тот же объект, что хранится в списке серверов
            // MainWindow, поэтому просто пишем конфиг рядом с сервером
            string configPath =
                Path.Combine(server.Path, "server.config");



            try
            {

                string json =
                    System.Text.Json.JsonSerializer.Serialize(
                        server,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                    );



                File.WriteAllText(configPath, json);



                MessageBox.Show("Домен сохранён.");

            }
            catch (Exception ex)
            {

                MessageBox.Show("Не удалось сохранить: " + ex.Message);

            }

        }








        private void StartServer_Click(
            object sender,
            RoutedEventArgs e)
        {

            try
            {

                consoleManager.StartServer(
                    server.Path,
                    server.Ram,
                    server.OnlineMode,
                    server.Core,
                    server.Version
                );


                InfoStatusText.Text = "🟡 Запускается...";

            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    ex.Message,
                    "Ошибка запуска"
                );

            }

        }








        private void StopServer_Click(
            object sender,
            RoutedEventArgs e)
        {

            consoleManager.StopServer();


            InfoStatusText.Text = "🔴 Остановлен";

        }








        private void SendCommand_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (string.IsNullOrWhiteSpace(CommandBox.Text))
                return;



            string command =
                CommandBox.Text;



            consoleManager.SendCommand(
                command
            );



            ConsoleOutput.Text +=
                "\n> " + command;



            CommandBox.Clear();

        }








        private void RefreshPlayers()
        {

            // Запоминаем, кто был выделен, чтобы не терять выделение
            // при обновлении списка (например, после клика Кик/Бан)
            string? selected =
                GetSelectedPlayer();



            PlayersListBox.Items.Clear();



            foreach (var player in playerManager.Players)
            {

                PlayersListBox.Items.Add(
                    player.Name
                );

            }



            if (selected != null &&
                PlayersListBox.Items.Contains(selected))
            {

                PlayersListBox.SelectedItem = selected;

            }

        }








        private void RefreshPlayers_Click(
            object sender,
            RoutedEventArgs e)
        {

            playerManager.Refresh();

        }








        private string? GetSelectedPlayer()
        {

            if (PlayersListBox.SelectedItem == null)
                return null;



            return PlayersListBox.SelectedItem.ToString();

        }








        private void Kick_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? player =
                GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            playerManager.Kick(player);

        }








        private void Ban_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? player =
                GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            playerManager.Ban(player);

        }




        private void BanIp_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? player =
                GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            string? ip =
                playerManager.GetKnownIp(player);



            if (ip == null)
            {

                MessageBox.Show(
                    "IP этого игрока пока неизвестен — он должен хотя бы " +
                    "раз зайти на сервер в текущей сессии, чтобы мы его увидели.",
                    "IP неизвестен"
                );


                return;

            }



            MessageBoxResult confirm =
                MessageBox.Show(
                    $"Забанить IP {ip} игрока {player}? Это заблокирует ЛЮБОЙ " +
                    "аккаунт, заходящий с этого IP, не только этот ник.",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            playerManager.BanIp(ip);

        }








        private void Op_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? player =
                GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            playerManager.GiveOperator(player);

        }








        private void Deop_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? player =
                GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            playerManager.RemoveOperator(player);

        }








        // ===================== ЗАБАНЕННЫЕ =====================


        private void RefreshBannedListBox()
        {

            string? selected =
                GetSelectedBannedPlayer();



            BannedListBox.Items.Clear();



            foreach (var name in playerManager.Banned)
            {

                BannedListBox.Items.Add(name);

            }



            if (selected != null &&
                BannedListBox.Items.Contains(selected))
            {

                BannedListBox.SelectedItem = selected;

            }

        }








        private string? GetSelectedBannedPlayer()
        {

            if (BannedListBox.SelectedItem == null)
                return null;



            return BannedListBox.SelectedItem.ToString();

        }








        private void Unban_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? player =
                GetSelectedBannedPlayer();



            if (player == null)
            {

                MessageBox.Show(
                    "Сначала выберите игрока из списка забаненных",
                    "Игрок не выбран"
                );


                return;

            }



            playerManager.Unban(player);

        }








        private void RefreshBanned_Click(
            object sender,
            RoutedEventArgs e)
        {

            playerManager.RefreshBanned();

        }




        // ===================== ЗАБАНЕННЫЕ IP =====================


        private void PlayerManager_BannedIpsUpdated()
        {

            Dispatcher.Invoke(() =>
            {

                RefreshBannedIpsListBox();

            });

        }




        private void RefreshBannedIpsListBox()
        {

            string? selected =
                GetSelectedBannedIp();



            BannedIpsListBox.Items.Clear();



            foreach (var ip in playerManager.BannedIps)
            {

                BannedIpsListBox.Items.Add(ip);

            }



            if (selected != null &&
                BannedIpsListBox.Items.Contains(selected))
            {

                BannedIpsListBox.SelectedItem = selected;

            }

        }




        private string? GetSelectedBannedIp()
        {

            if (BannedIpsListBox.SelectedItem == null)
                return null;



            return BannedIpsListBox.SelectedItem.ToString();

        }




        private void UnbanIp_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? ip =
                GetSelectedBannedIp();



            if (ip == null)
            {

                MessageBox.Show(
                    "Сначала выберите IP из списка забаненных",
                    "IP не выбран"
                );


                return;

            }



            playerManager.UnbanIp(ip);

        }




        // ===================== МИР (СКАЧАТЬ / ЗАГРУЗИТЬ) =====================


        private void DownloadWorld_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (consoleManager.IsRunning)
            {

                MessageBox.Show(
                    "Сначала останови сервер — файлы мира могут быть " +
                    "заняты работающим процессом.",
                    "Сервер запущен"
                );


                return;

            }



            string worldPath =
                Path.Combine(server.Path, "world");



            if (!Directory.Exists(worldPath))
            {

                MessageBox.Show(
                    "Папка мира ещё не создана — запусти сервер хотя бы " +
                    "раз, чтобы мир сгенерировался.",
                    "Мир не найден"
                );


                return;

            }



            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "ZIP-архив (*.zip)|*.zip",
                FileName = server.Name + "_world.zip"
            };



            if (dialog.ShowDialog() != true)
                return;



            try
            {

                if (File.Exists(dialog.FileName))
                {
                    File.Delete(dialog.FileName);
                }



                ZipFile.CreateFromDirectory(worldPath, dialog.FileName);



                MessageBox.Show("Мир сохранён в " + dialog.FileName);

            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    "Не удалось сохранить мир: " + ex.Message,
                    "Ошибка"
                );

            }

        }




        private void UploadWorld_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (consoleManager.IsRunning)
            {

                MessageBox.Show(
                    "Сначала останови сервер перед загрузкой другого мира.",
                    "Сервер запущен"
                );


                return;

            }



            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "ZIP-архив (*.zip)|*.zip"
            };



            if (dialog.ShowDialog() != true)
                return;



            MessageBoxResult confirm =
                MessageBox.Show(
                    "Текущий мир будет ПОЛНОСТЬЮ заменён загруженным. " +
                    "Это необратимо. Продолжить?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            string worldPath =
                Path.Combine(server.Path, "world");



            try
            {

                if (Directory.Exists(worldPath))
                {
                    Directory.Delete(worldPath, true);
                }



                ZipFile.ExtractToDirectory(dialog.FileName, worldPath);



                MessageBox.Show("Мир загружен и заменён.");

            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    "Не удалось загрузить мир: " + ex.Message,
                    "Ошибка"
                );

            }

        }




        // ===================== ЗАБАНЕННЫЕ IP =====================


       




  

        // ===================== МИР =====================


 





        // ===================== WHITELIST =====================


        private void RefreshWhitelist()
        {

            string? selected =
                GetSelectedWhitelistPlayer();



            WhitelistListBox.Items.Clear();



            foreach (var name in whitelistManager.Players)
            {

                WhitelistListBox.Items.Add(name);

            }



            if (selected != null &&
                WhitelistListBox.Items.Contains(selected))
            {

                WhitelistListBox.SelectedItem = selected;

            }

        }








        private string? GetSelectedWhitelistPlayer()
        {

            if (WhitelistListBox.SelectedItem == null)
                return null;



            return WhitelistListBox.SelectedItem.ToString();

        }








        private void WhitelistAdd_Click(
            object sender,
            RoutedEventArgs e)
        {

            string name =
                WhitelistNameBox.Text.Trim();



            if (string.IsNullOrWhiteSpace(name))
            {

                MessageBox.Show(
                    "Введите ник игрока",
                    "Пустое поле"
                );


                return;

            }



            whitelistManager.Add(name);


            WhitelistNameBox.Clear();

        }








        private void WhitelistRemove_Click(
            object sender,
            RoutedEventArgs e)
        {

            string? player =
                GetSelectedWhitelistPlayer();



            if (player == null)
            {

                MessageBox.Show(
                    "Сначала выберите игрока из белого списка",
                    "Игрок не выбран"
                );


                return;

            }



            whitelistManager.Remove(player);

        }








        private void WhitelistRefresh_Click(
            object sender,
            RoutedEventArgs e)
        {

            whitelistManager.Refresh();

        }








        private void WhitelistEnabledCheckBox_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (WhitelistEnabledCheckBox.IsChecked == true)
            {

                whitelistManager.Enable();

            }
            else
            {

                whitelistManager.Disable();

            }

        }


    }
}