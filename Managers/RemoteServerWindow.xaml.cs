using MCServerManager.Managers;
using System;
using System.Collections.Generic;
using System.Windows;

namespace MCServerManager
{
    public partial class RemoteServerWindow : Window
    {

        private NetworkClient client;



        public RemoteServerWindow(NetworkClient connectedClient)
        {
            InitializeComponent();


            client = connectedClient;



            Title =
                "Удалённое управление: " + client.ServerName;



            ConnectionStatusText.Text =
                "Подключено к: " + client.ServerName;



            client.ConsoleReceived +=
                Client_ConsoleReceived;



            client.PlayersReceived +=
                Client_PlayersReceived;



            client.WhitelistReceived +=
                Client_WhitelistReceived;



            client.BannedReceived +=
                Client_BannedReceived;



            client.StatusReceived +=
                Client_StatusReceived;



            client.Disconnected +=
                Client_Disconnected;



            // При закрытии окна рвём сетевое соединение, но НЕ трогаем сам
            // сервер — им продолжает управлять хозяин
            Closing +=
                (s, e) => client.Dispose();



            client.RefreshPlayers();

            client.RefreshWhitelist();

            client.RefreshBanned();

        }








        private void Client_ConsoleReceived(string message)
        {

            Dispatcher.Invoke(() =>
            {

                ConsoleOutput.Text +=
                    message + "\n";


                ConsoleOutput.ScrollToEnd();

            });

        }








        private void Client_PlayersReceived(List<string> players)
        {

            Dispatcher.Invoke(() =>
            {

                string? selected =
                    GetSelectedPlayer();



                PlayersListBox.Items.Clear();



                foreach (string name in players)
                {

                    PlayersListBox.Items.Add(name);

                }



                if (selected != null && PlayersListBox.Items.Contains(selected))
                {

                    PlayersListBox.SelectedItem = selected;

                }

            });

        }








        private void Client_WhitelistReceived(List<string> names)
        {

            Dispatcher.Invoke(() =>
            {

                WhitelistListBox.Items.Clear();



                foreach (string name in names)
                {

                    WhitelistListBox.Items.Add(name);

                }

            });

        }








        private void Client_BannedReceived(List<string> names)
        {

            Dispatcher.Invoke(() =>
            {

                BannedListBox.Items.Clear();



                foreach (string name in names)
                {

                    BannedListBox.Items.Add(name);

                }

            });

        }








        private void Client_StatusReceived(bool running)
        {

            Dispatcher.Invoke(() =>
            {

                ConnectionStatusText.Text =
                    "Подключено к: " + client.ServerName +
                    (running ? " (запущен)" : " (остановлен)");

            });

        }








        private void Client_Disconnected()
        {

            Dispatcher.Invoke(() =>
            {

                ConnectionStatusText.Text =
                    "Соединение потеряно";



                MessageBox.Show(
                    "Соединение с другом разорвано.",
                    "Отключено"
                );

            });

        }








        private void SendCommand_Click(object sender, RoutedEventArgs e)
        {

            if (string.IsNullOrWhiteSpace(CommandBox.Text))
                return;



            client.SendCommand(CommandBox.Text);



            ConsoleOutput.Text +=
                "\n> " + CommandBox.Text;



            CommandBox.Clear();

        }








        private void StartServer_Click(object sender, RoutedEventArgs e)
        {

            client.Start();

        }








        private void StopServer_Click(object sender, RoutedEventArgs e)
        {

            client.Stop();

        }








        private string? GetSelectedPlayer()
        {

            if (PlayersListBox.SelectedItem == null)
                return null;



            return PlayersListBox.SelectedItem.ToString();

        }








        private void Kick_Click(object sender, RoutedEventArgs e)
        {

            string? player = GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            client.Kick(player);

        }








        private void Ban_Click(object sender, RoutedEventArgs e)
        {

            string? player = GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            client.Ban(player);

        }








        private void Op_Click(object sender, RoutedEventArgs e)
        {

            string? player = GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            client.Op(player);

        }








        private void Deop_Click(object sender, RoutedEventArgs e)
        {

            string? player = GetSelectedPlayer();



            if (player == null)
            {
                MessageBox.Show("Сначала выберите игрока из списка.", "Игрок не выбран");
                return;
            }



            client.Deop(player);

        }








        private void RefreshPlayers_Click(object sender, RoutedEventArgs e)
        {

            client.RefreshPlayers();

        }








        private void WhitelistOn_Click(object sender, RoutedEventArgs e)
        {

            client.WhitelistEnable();

        }








        private void WhitelistOff_Click(object sender, RoutedEventArgs e)
        {

            client.WhitelistDisable();

        }








        private void WhitelistAdd_Click(object sender, RoutedEventArgs e)
        {

            string name =
                WhitelistNameBox.Text.Trim();



            if (string.IsNullOrWhiteSpace(name))
            {

                MessageBox.Show("Введите ник игрока", "Пустое поле");


                return;

            }



            client.WhitelistAdd(name);


            WhitelistNameBox.Clear();

        }








        private void WhitelistRemove_Click(object sender, RoutedEventArgs e)
        {

            if (WhitelistListBox.SelectedItem == null)
            {

                MessageBox.Show(
                    "Сначала выберите игрока из белого списка",
                    "Игрок не выбран"
                );


                return;

            }



            client.WhitelistRemove(WhitelistListBox.SelectedItem.ToString()!);

        }








        private void Unban_Click(object sender, RoutedEventArgs e)
        {

            if (BannedListBox.SelectedItem == null)
            {

                MessageBox.Show(
                    "Сначала выберите игрока из списка забаненных",
                    "Игрок не выбран"
                );


                return;

            }



            client.Unban(BannedListBox.SelectedItem.ToString()!);

        }

    }
}