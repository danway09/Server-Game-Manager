using MCServerManager.Managers;
using MCServerManager.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MCServerManager
{
    public partial class MainWindow : Window
    {

        private ServerManager serverManager;
        private ConsoleManager consoleManager;
        private PlayerManager? playerManager;
        private WhitelistManager? whitelistManager;
        private CoreDownloader coreDownloader;
        private FabricDownloader fabricDownloader;
        private ForgeDownloader forgeDownloader = new();
        private NeoForgeDownloader neoForgeDownloader = new();
        private VanillaDownloader vanillaDownloader = new();
        private BukkitDownloader bukkitDownloader = new();

        private NetworkHost? networkHost;

        private CancellationTokenSource? versionLoadCts;

        private MinecraftServer? selectedServer;

        // Тот сервер, который РЕАЛЬНО запущен через consoleManager сейчас
        // (может отличаться от selectedServer, если пользователь выбрал
        // другой сервер в списке, не останавливая текущий) — нужен для
        // иконок статуса в списке и для автоперезапуска при краше
        private MinecraftServer? runningMcServer;

        // ===================== TERRARIA =====================

        private TerrariaServerManager terrariaServerManager = new();
        private TerrariaConsoleManager terrariaConsoleManager = new();
        private TShockDownloader tshockDownloader = new();
        private TModLoaderDownloader tModLoaderDownloader = new();
        private TerrariaServer? selectedTerrariaServer;

        private TerrariaServer? runningTerrariaServer;
        private TerrariaPlayerManager terrariaPlayerManager = null!;
        private bool isTerrariaMode = false;




        public MainWindow()
        {
            InitializeComponent();

            UpdateThemeButton();


            serverManager = new ServerManager();

            consoleManager = new ConsoleManager();

            consoleManager.ConsoleReceived +=
    text =>
    {
        Dispatcher.Invoke(() =>
        {
            ConsoleBox.Text +=
                text + "\n";


            ConsoleBox.ScrollToEnd();
        });
    };


            consoleManager.ServerReady +=
                () =>
                {

                    Dispatcher.Invoke(() =>
                    {

                        InfoStatusText.Text = "🟢 Запущен";



                        MessageBox.Show(
                            (selectedServer?.Name ?? "Сервер") +
                            " запущен и готов принимать подключения!",
                            "Сервер готов",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );

                    });

                };



            // Автоперезапуск при краше — срабатывает только если процесс
            // завершился САМ (не через наши же кнопки "Остановить"/"Удалить")
            consoleManager.UnexpectedExit +=
                () =>
                {

                    Dispatcher.Invoke(() =>
                    {

                        MinecraftServer? crashed = runningMcServer;


                        runningMcServer = null;


                        LoadServers();



                        if (crashed == null)
                            return;



                        if (McAutoRestartCheckBox.IsChecked != true)
                        {

                            InfoStatusText.Text = "🔴 Остановлен (упал)";


                            return;

                        }



                        InfoStatusText.Text = "🟡 Сервер упал — перезапускаю...";



                        try
                        {

                            consoleManager.StartServer(
                                crashed.Path,
                                crashed.Ram,
                                crashed.OnlineMode,
                                crashed.Core,
                                crashed.Version
                            );



                            runningMcServer = crashed;


                            LoadServers();

                        }
                        catch (Exception ex)
                        {

                            MessageBox.Show(
                                "Сервер упал и не удалось перезапустить автоматически: " + ex.Message,
                                "Автоперезапуск не удался"
                            );

                        }

                    });

                };


            // Terraria-консоль пишет в тот же ConsoleBox, что и Minecraft —
            // видно только когда реально выбран Terraria-сервер, но
            // события подписываем сразу, чтобы ничего не пропустить
            terrariaConsoleManager.ConsoleReceived +=
                text =>
                {
                    Dispatcher.Invoke(() =>
                    {

                        if (isTerrariaMode)
                        {

                            ConsoleBox.Text += text + "\n";


                            ConsoleBox.ScrollToEnd();

                        }

                    });
                };


            terrariaConsoleManager.ServerReady +=
                () =>
                {
                    Dispatcher.Invoke(() =>
                    {

                        TerrariaInfoStatusText.Text = "🟢 Запущен";



                        MessageBox.Show(
                            (selectedTerrariaServer?.Name ?? "Сервер") +
                            " запущен и готов принимать подключения!",
                            "Сервер готов",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );

                    });
                };



            terrariaConsoleManager.UnexpectedExit +=
                () =>
                {

                    Dispatcher.Invoke(() =>
                    {

                        TerrariaServer? crashed = runningTerrariaServer;


                        runningTerrariaServer = null;


                        LoadServers();



                        if (crashed == null)
                            return;



                        if (TrAutoRestartCheckBox.IsChecked != true)
                        {

                            TerrariaInfoStatusText.Text = "🔴 Остановлен (упал)";


                            return;

                        }



                        TerrariaInfoStatusText.Text = "🟡 Сервер упал — перезапускаю...";



                        try
                        {

                            terrariaConsoleManager.StartServer(crashed);



                            runningTerrariaServer = crashed;


                            LoadServers();

                        }
                        catch (Exception ex)
                        {

                            MessageBox.Show(
                                "Сервер упал и не удалось перезапустить автоматически: " + ex.Message,
                                "Автоперезапуск не удался"
                            );

                        }

                    });

                };



            terrariaPlayerManager = new TerrariaPlayerManager(terrariaConsoleManager);


            terrariaPlayerManager.PlayersUpdated +=
                () =>
                {
                    Dispatcher.Invoke(() =>
                    {

                        TerrariaPlayersListBox.Items.Clear();



                        foreach (var name in terrariaPlayerManager.Players)
                        {
                            TerrariaPlayersListBox.Items.Add(name);
                        }

                    });
                };


            coreDownloader = new CoreDownloader();

            fabricDownloader =
    new FabricDownloader();


            LoadServers();



            // Список версий грузим после того, как окно полностью
            // прогрузилось (CoreVersionBox уже точно существует к этому
            // моменту) — при старте SelectionChanged срабатывает слишком
            // рано, ещё внутри InitializeComponent()
            Loaded +=
                async (s, e) => await LoadVersionsForSelectedCore();



            // Раньше закрытие программы просто убивало все запущенные
            // сервера через Job Object без вопросов — легко случайно
            // прикрыть сервер прямо во время игры. Теперь спрашиваем.
            Closing += MainWindow_Closing;

        }




        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {

            bool mcRunning = consoleManager.IsRunning;

            bool terrariaRunning = terrariaConsoleManager.IsRunning;



            if (!mcRunning && !terrariaRunning)
                return;



            string runningNames = "";



            if (mcRunning)
            {
                runningNames += $"Minecraft-сервер \"{runningMcServer?.Name}\"";
            }



            if (terrariaRunning)
            {

                if (runningNames.Length > 0)
                {
                    runningNames += " и ";
                }



                runningNames += $"Terraria-сервер \"{runningTerrariaServer?.Name}\"";

            }



            MessageBoxResult confirm =
                MessageBox.Show(
                    $"Сейчас запущен {runningNames}. Если закроешь программу, " +
                    "сервер тоже остановится и все игроки будут отключены.\n\n" +
                    "Точно закрыть?",
                    "Сервер всё ещё работает",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
            {

                e.Cancel = true;

            }

        }






        private void LoadServers()
        {
            ServersListBox.Items.Clear();


            if (isTerrariaMode)
            {

                foreach (var server in terrariaServerManager.Servers)
                {

                    bool running =
                        terrariaConsoleManager.IsRunning &&
                        runningTerrariaServer != null &&
                        runningTerrariaServer.Name == server.Name;



                    ServersListBox.Items.Add(
                        (running ? "🟢 " : "🔴 ") + server.Name
                    );

                }

                return;

            }



            foreach (var server in serverManager.Servers)
            {

                bool running =
                    consoleManager.IsRunning &&
                    runningMcServer != null &&
                    runningMcServer.Name == server.Name;



                ServersListBox.Items.Add(
                    (running ? "🟢 " : "🔴 ") + server.Name
                );

            }
        }




        // Список серверов теперь показывает иконку статуса перед именем
        // ("🟢 Имя" / "🔴 Имя") — эта функция убирает иконку и пробел,
        // чтобы получить настоящее имя сервера для поиска в менеджере
        private static string StripStatusIcon(string listItem)
        {

            int spaceIndex = listItem.IndexOf(' ');



            if (spaceIndex == -1)
                return listItem;



            return listItem.Substring(spaceIndex + 1);

        }




        // ===================== ПЕРЕКЛЮЧАТЕЛЬ ИГРЫ =====================


        private void SelectGameMinecraft_Click(object sender, RoutedEventArgs e)
        {

            isTerrariaMode = false;



            MinecraftControlsPanel.Visibility = Visibility.Visible;

            TerrariaControlsPanel.Visibility = Visibility.Collapsed;

            CoreSupportText.Visibility = Visibility.Visible;



            // Показываем MC-вкладки, прячем Terraria-вкладки
            MinecraftInfoTab.Visibility = Visibility.Visible;

            TerrariaInfoTab.Visibility = Visibility.Collapsed;

            MinecraftSettingsTab.Visibility = Visibility.Visible;

            TerrariaSettingsTab.Visibility = Visibility.Collapsed;

            AccessTab.Visibility = Visibility.Visible;



            selectedServer = null;

            selectedTerrariaServer = null;


            ServerTitle.Text = "Сервер не выбран";

            ConsoleBox.Text = "";


            LoadServers();

        }




        private void SelectGameTerraria_Click(object sender, RoutedEventArgs e)
        {

            isTerrariaMode = true;



            MinecraftControlsPanel.Visibility = Visibility.Collapsed;

            TerrariaControlsPanel.Visibility = Visibility.Visible;

            CoreSupportText.Visibility = Visibility.Collapsed;



            // Показываем Terraria-вкладки, прячем MC-вкладки. "Доступ"
            // (сетевой хостинг) для Terraria пока не реализован — прячем.
            MinecraftInfoTab.Visibility = Visibility.Collapsed;

            TerrariaInfoTab.Visibility = Visibility.Visible;

            MinecraftSettingsTab.Visibility = Visibility.Collapsed;

            TerrariaSettingsTab.Visibility = Visibility.Visible;

            AccessTab.Visibility = Visibility.Collapsed;



            selectedServer = null;

            selectedTerrariaServer = null;


            ServerTitle.Text = "Сервер не выбран";

            ConsoleBox.Text = "";


            LoadServers();

        }




        // ===================== ВКЛАДКА "ИНФОРМАЦИЯ" =====================


        private async Task LoadPublicIpAsync()
        {

            if (selectedServer == null)
                return;



            string ip =
                await NetworkInfo.GetPublicIpAsync();



            InfoPublicIpBox.Text =
                $"{ip}:{selectedServer.Port}";

        }




        private async void RefreshPublicIp_Click(object sender, RoutedEventArgs e)
        {

            InfoPublicIpBox.Text = "определяется...";



            await LoadPublicIpAsync();

        }




        private void SaveDomain_Click(object sender, RoutedEventArgs e)
        {

            if (selectedServer == null)
                return;



            selectedServer.Domain =
                InfoDomainBox.Text.Trim();



            serverManager.SaveConfig(selectedServer);



            MessageBox.Show("Домен сохранён.");

        }




        private void DownloadWorld_Click(object sender, RoutedEventArgs e)
        {

            if (selectedServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



            if (consoleManager.IsRunning)
            {

                MessageBox.Show(
                    "Сначала останови сервер — файлы мира могут быть заняты.",
                    "Сервер запущен"
                );


                return;

            }



            string worldPath =
                Path.Combine(selectedServer.Path, "world");



            if (!Directory.Exists(worldPath))
            {

                MessageBox.Show(
                    "Папка мира ещё не создана — запусти сервер хотя бы раз.",
                    "Мир не найден"
                );


                return;

            }



            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "ZIP-архив (*.zip)|*.zip",
                FileName = selectedServer.Name + "_world.zip"
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

                MessageBox.Show("Не удалось сохранить мир: " + ex.Message, "Ошибка");

            }

        }




        private void UploadWorld_Click(object sender, RoutedEventArgs e)
        {

            if (selectedServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



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
                    "Текущий мир будет ПОЛНОСТЬЮ заменён загруженным. Это необратимо. Продолжить?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            string worldPath =
                Path.Combine(selectedServer.Path, "world");



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

                MessageBox.Show("Не удалось загрузить мир: " + ex.Message, "Ошибка");

            }

        }







        private void CreateServer_Click(
    object sender,
    RoutedEventArgs e)
        {

            if (string.IsNullOrWhiteSpace(ServerNameBox.Text))
            {
                MessageBox.Show(
                    "Введите название сервера"
                );

                return;
            }



            if (isTerrariaMode)
            {

                TerrariaServer terrariaServer =
                    new TerrariaServer
                    {
                        Name = ServerNameBox.Text
                    };



                if (terrariaServerManager.CreateServer(terrariaServer))
                {

                    LoadServers();


                    MessageBox.Show("Сервер создан! Теперь установи TShock.");

                }
                else
                {

                    MessageBox.Show("Сервер с таким названием уже существует");

                }



                return;

            }



            MinecraftServer server =
                new MinecraftServer()
                {
                    Name = ServerNameBox.Text,

                    Core = "Paper",

                    Version = "1.21.8",

                    Ram = 2048

                    // OnlineMode берётся из дефолта модели (true) —
                    // пиратский режим включается/выключается во вкладке
                    // "Настройки" после создания, отдельного чекбокса
                    // при создании больше нет (был дублем)
                };



            if (serverManager.CreateServer(server))
            {
                LoadServers();


                MessageBox.Show(
                    "Сервер создан!"
                );
            }
            else
            {
                MessageBox.Show(
                    "Сервер с таким названием уже существует"
                );
            }

        }







        private void StartServer_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (consoleManager.IsRunning)
            {
                MessageBox.Show(
                    "Сервер уже запущен!"
                );

                return;
            }

            if (selectedServer == null)
            {
                MessageBox.Show(
                    "Выбери сервер"
                );

                return;
            }



            if (!File.Exists(
                Path.Combine(
                    selectedServer.Path,
                    "server.jar")))
            {
                MessageBox.Show(
                    "Сначала установи ядро"
                );

                return;
            }




            try
            {

                consoleManager.StartServer(
                    selectedServer.Path,
                    selectedServer.Ram,
                    selectedServer.OnlineMode,
                    selectedServer.Core,
                    selectedServer.Version
                );

            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    ex.Message,
                    "Ошибка запуска"
                );

                return;

            }


            selectedServer.Start();


            serverManager.SaveConfig(
                selectedServer
            );


            runningMcServer = selectedServer;


            ServerTitle.Text =
                selectedServer.Name +
                " | Запущен";


            InfoStatusText.Text = "🟡 Запускается...";


            LoadServers();

        }




        private void StopServer_Click(
     object sender,
     RoutedEventArgs e)
        {

            MessageBoxResult confirm =
                MessageBox.Show(
                    "Остановить сервер? Все игроки будут отключены.",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            try
            {

                consoleManager.StopServer();


                runningMcServer = null;



                if (selectedServer != null)
                {

                    selectedServer.Stop();


                    serverManager.SaveConfig(
                        selectedServer
                    );


                    ServerTitle.Text =
                        selectedServer.Name +
                        " | Остановлен";


                    InfoStatusText.Text = "🔴 Остановлен";

                }



                LoadServers();


            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    ex.Message
                );

            }

        }








        // перезапуск: аккуратно останавливаем и запускаем заново,
        // тяжёлая часть (ожидание остановки java) уходит в фон,
        // чтобы окно не зависало на время остановки

        private async void RestartServer_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (selectedServer == null)
            {
                MessageBox.Show(
                    "Сначала выбери сервер"
                );

                return;
            }



            if (!consoleManager.IsRunning)
            {
                MessageBox.Show(
                    "Сервер сейчас не запущен — нечего перезапускать"
                );

                return;
            }



            ServerTitle.Text =
                selectedServer.Name + " | Перезапуск...";



            await Task.Run(() =>
            {
                consoleManager.StopServer();
            });



            try
            {

                consoleManager.StartServer(
                    selectedServer.Path,
                    selectedServer.Ram,
                    selectedServer.OnlineMode,
                    selectedServer.Core,
                    selectedServer.Version
                );



                selectedServer.Start();


                serverManager.SaveConfig(
                    selectedServer
                );


                runningMcServer = selectedServer;


                ServerTitle.Text =
                    selectedServer.Name + " | Запущен";


                LoadServers();

            }
            catch (Exception ex)
            {

                selectedServer.Stop();


                runningMcServer = null;


                serverManager.SaveConfig(
                    selectedServer
                );


                ServerTitle.Text =
                    selectedServer.Name + " | Остановлен";


                LoadServers();


                MessageBox.Show(
                    ex.Message,
                    "Ошибка перезапуска"
                );

            }

        }








        private void OpenFolder_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (selectedServer == null)
                return;


            Process.Start(
                "explorer.exe",
                selectedServer.Path
            );

        }








        private void ServersListBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {

            if (ServersListBox.SelectedItem == null)
                return;



            string name =
                StripStatusIcon(ServersListBox.SelectedItem.ToString()!);



            if (isTerrariaMode)
            {

                selectedTerrariaServer =
                    terrariaServerManager.GetServer(name);



                if (selectedTerrariaServer != null)
                {

                    ServerTitle.Text = selectedTerrariaServer.Name;

                    ConsoleBox.Text = selectedTerrariaServer.GetInfo();


                    TerrariaPortBox.Text = selectedTerrariaServer.Port.ToString();

                    TerrariaMaxPlayersBox.Text = selectedTerrariaServer.MaxPlayers.ToString();

                    TerrariaPasswordBox.Text = selectedTerrariaServer.Password;


                    foreach (ComboBoxItem item in TerrariaWorldSizeBox.Items)
                    {
                        if ((string)item.Tag == selectedTerrariaServer.WorldSize.ToString())
                        {
                            TerrariaWorldSizeBox.SelectedItem = item;
                            break;
                        }
                    }


                    foreach (ComboBoxItem item in TerrariaDifficultyBox.Items)
                    {
                        if ((string)item.Tag == selectedTerrariaServer.Difficulty.ToString())
                        {
                            TerrariaDifficultyBox.SelectedItem = item;
                            break;
                        }
                    }


                    foreach (ComboBoxItem item in TerrariaCoreBox.Items)
                    {
                        if (item.Content.ToString() == selectedTerrariaServer.ServerCore)
                        {
                            TerrariaCoreBox.SelectedItem = item;
                            break;
                        }
                    }


                    TShockVersionBox.Text = selectedTerrariaServer.TShockVersion;



                    TerrariaInfoStatusText.Text =
                        terrariaConsoleManager.IsRunning ? "🟢 Запущен" : "🔴 Остановлен";


                    TerrariaInfoLocalIpBox.Text =
                        $"localhost:{selectedTerrariaServer.Port}";


                    TerrariaInfoVersionBox.Text =
                        string.IsNullOrWhiteSpace(selectedTerrariaServer.TShockVersion)
                            ? "не установлен"
                            : selectedTerrariaServer.ServerCore + " (" + selectedTerrariaServer.TShockVersion + ")";


                    _ = LoadTerrariaPublicIpAsync();


                    // список игроков этого сервера мы не храним между
                    // переключениями — начинаем с чистого листа, реальные
                    // онлайн-игроки появятся заново по мере захода
                    terrariaPlayerManager.Clear();

                }



                return;

            }



            selectedServer =
                serverManager.GetServer(name);




            if (selectedServer != null)
            {

                ServerTitle.Text =
                    selectedServer.Name;



                ConsoleBox.Text =
                    selectedServer.GetInfo();



                InfoStatusText.Text =
                    consoleManager.IsRunning ? "🟢 Запущен" : "🔴 Остановлен";



                InfoLocalIpBox.Text =
                    $"localhost:{selectedServer.Port}";



                InfoVersionBox.Text =
                    selectedServer.Version;



                InfoCoreBox.Text =
                    selectedServer.Core;



                InfoDomainBox.Text =
                    selectedServer.Domain;



                _ = LoadPublicIpAsync();



                LoadSettingsFields();



                CoreVersionBox.Text =
                    selectedServer.Version;



                foreach (ComboBoxItem item in CoreComboBox.Items)
                {

                    if (item.Content.ToString() == selectedServer.Core)
                    {

                        CoreComboBox.SelectedItem = item;


                        break;

                    }

                }

            }

        }








        // Подгружает текущие значения server.properties в поля вкладки
        // "Настройки" для выбранного сервера
        private void LoadSettingsFields()
        {

            if (selectedServer == null)
                return;



            SettingsNameBox.Text =
                selectedServer.Name;



            SettingsViewDistanceBox.Text =
                ServerProperties.Get(selectedServer.Path, "view-distance", "10");



            SettingsSimulationDistanceBox.Text =
                ServerProperties.Get(selectedServer.Path, "simulation-distance", "10");



            SettingsMaxPlayersBox.Text =
                ServerProperties.Get(selectedServer.Path, "max-players", "20");



            string difficulty =
                ServerProperties.Get(selectedServer.Path, "difficulty", "easy");



            foreach (ComboBoxItem item in SettingsDifficultyCombo.Items)
            {

                if ((string)item.Tag == difficulty)
                {

                    SettingsDifficultyCombo.SelectedItem = item;


                    break;

                }

            }



            SettingsPirateCheckBox.IsChecked =
                !selectedServer.OnlineMode;



            SettingsSeedBox.Text =
                ServerProperties.Get(selectedServer.Path, "level-seed", "");



            string pvp =
                ServerProperties.Get(selectedServer.Path, "pvp", "true");



            SettingsPvpCheckBox.IsChecked =
                pvp.Equals("true", StringComparison.OrdinalIgnoreCase);

        }








        private void OpenServerWindow_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (selectedServer == null)
            {
                MessageBox.Show(
                    "Сначала выбери сервер"
                );

                return;
            }



            ServerWindow window =
                new ServerWindow(
                    selectedServer,
                    consoleManager
                );


            window.Show();

        }








        // установка Fabric вручную

        // ГЛАВНЫЙ ФИКС: раньше эта кнопка вообще не была подключена к
        // автозагрузке — она просто открывала диалог "выбери jar руками",
        // а Core в подписи брался из комбобокса что бы ты туда ни выбрал
        // фактически файлом. Теперь кнопка реально качает и ставит именно
        // то ядро и версию, что выбраны в форме.
        private async void InstallCore_Click(
     object sender,
     RoutedEventArgs e)
        {

            if (selectedServer == null)
            {
                MessageBox.Show(
                    "Сначала выбери сервер"
                );

                return;
            }



            if (consoleManager.IsRunning)
            {
                MessageBox.Show(
                    "Сначала останови сервер перед сменой ядра."
                );

                return;
            }



            string core =
                ((ComboBoxItem)CoreComboBox.SelectedItem)
                .Content
                .ToString()!;



            string version =
                CoreVersionBox.Text.Trim();



            if (string.IsNullOrWhiteSpace(version))
            {
                MessageBox.Show(
                    "Укажи версию Minecraft (например, 1.21.8)"
                );

                return;
            }



            InstallCoreButton.IsEnabled = false;

            InstallCoreButton.Content = "Устанавливаю...";



            InstallProgressWindow progressWindow =
                new InstallProgressWindow($"Установка {core} {version}...")
                {
                    Owner = this
                };


            progressWindow.Show();


            progressWindow.AppendLine($"Начинаю установку {core} {version}...");



            try
            {

                switch (core)
                {

                    case "Paper":

                        progressWindow.AppendLine("Скачиваю Paper...");

                        await coreDownloader.DownloadPaper(selectedServer.Path, version);

                        break;



                    case "Purpur":

                        progressWindow.AppendLine("Скачиваю Purpur...");

                        await coreDownloader.DownloadPurpur(selectedServer.Path, version);

                        break;



                    case "Bukkit":

                        progressWindow.AppendLine(
                            "Bukkit собирается из исходников через BuildTools — " +
                            "это ДОЛГО (5-15 минут), просто подожди."
                        );

                        await bukkitDownloader.InstallBukkit(
                            selectedServer.Path,
                            version,
                            line => progressWindow.AppendLine(line)
                        );

                        break;



                    case "Fabric":

                        progressWindow.AppendLine("Скачиваю и устанавливаю Fabric...");

                        await fabricDownloader.InstallFabric(selectedServer.Path, version);

                        break;



                    case "Forge":

                        progressWindow.AppendLine("Скачиваю и устанавливаю Forge...");

                        await forgeDownloader.InstallForge(selectedServer.Path, version, line => progressWindow.AppendLine(line));

                        break;



                    case "NeoForge":

                        progressWindow.AppendLine("Скачиваю и устанавливаю NeoForge...");

                        await neoForgeDownloader.InstallNeoForge(selectedServer.Path, version);

                        break;



                    case "Vanilla":

                        progressWindow.AppendLine("Скачиваю Vanilla...");

                        await vanillaDownloader.DownloadVanilla(selectedServer.Path, version);

                        break;

                }



                // ГЛАВНЫЙ ФИКС: раньше "успех" означал просто "не вылетело
                // исключение" — а инсталлятор мог тихо ничего не сделать
                // (например, из-за скрытой ошибки внутри самого java-процесса,
                // которую мы не поймали). Теперь реально проверяем, что
                // ожидаемые файлы появились на диске. Для Forge учитываем
                // оба варианта установки: новый (run.bat/run.sh) и старый
                // (forge-*.jar) — старые сборки не создают run.bat, и раньше
                // это ошибочно считалось провалом.
                bool installedOk;
                string expectedFile;

                if (core == "Forge")
                {
                    installedOk = ForgeInstallStatus.IsInstalled(selectedServer.Path);
                    expectedFile = "run.bat или forge-*.jar";
                }
                else if (core == "NeoForge")
                {
                    installedOk =
                        File.Exists(Path.Combine(selectedServer.Path, "run.bat")) ||
                        File.Exists(Path.Combine(selectedServer.Path, "run.sh"));
                    expectedFile = "run.bat";
                }
                else
                {
                    installedOk = File.Exists(Path.Combine(selectedServer.Path, "server.jar"));
                    expectedFile = "server.jar";
                }

                if (!installedOk)
                {
                    throw new Exception(
                        $"Установка прошла без явных ошибок, но ожидаемый файл " +
                        $"\"{expectedFile}\" так и не появился в папке сервера. " +
                        "Похоже, инсталлятор упал молча — проверь консоль/сеть " +
                        "и попробуй ещё раз."
                    );
                }



                selectedServer.Core = core;

                selectedServer.Version = version;



                serverManager.SaveConfig(selectedServer);



                MessageBox.Show(
                    $"Ядро {core} {version} успешно установлено!"
                );

            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    ex.Message,
                    "Ошибка установки ядра"
                );

            }
            finally
            {

                InstallCoreButton.IsEnabled = true;

                InstallCoreButton.Content = "Установить ядро";


                progressWindow.Close();

            }

        }








        // ===================== НАСТРОЙКИ =====================


        private void SaveSettings_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (selectedServer == null)
            {

                MessageBox.Show(
                    "Сначала выбери сервер"
                );


                return;

            }



            // Проверяем числовые поля перед сохранением

            if (!int.TryParse(SettingsViewDistanceBox.Text, out int viewDistance) ||
                viewDistance < 2 || viewDistance > 32)
            {

                MessageBox.Show(
                    "Дистанция прорисовки должна быть числом от 2 до 32",
                    "Некорректное значение"
                );


                return;

            }



            if (!int.TryParse(SettingsSimulationDistanceBox.Text, out int simDistance) ||
                simDistance < 2 || simDistance > 32)
            {

                MessageBox.Show(
                    "Дальность симуляции должна быть числом от 2 до 32",
                    "Некорректное значение"
                );


                return;

            }



            if (!int.TryParse(SettingsMaxPlayersBox.Text, out int maxPlayers) ||
                maxPlayers < 1)
            {

                MessageBox.Show(
                    "Максимум игроков должен быть положительным числом",
                    "Некорректное значение"
                );


                return;

            }



            if (SettingsDifficultyCombo.SelectedItem == null)
            {

                MessageBox.Show(
                    "Выбери сложность"
                );


                return;

            }



            string difficulty =
                (string)((ComboBoxItem)SettingsDifficultyCombo.SelectedItem).Tag;



            bool isPirate =
                SettingsPirateCheckBox.IsChecked == true;



            // Если сид не указан — подставляем случайное число, чтобы не
            // плодить сервера с одним и тем же дефолтным (пустым) сидом
            string seed =
                SettingsSeedBox.Text.Trim();



            if (string.IsNullOrEmpty(seed))
            {

                seed = new Random().NextInt64().ToString();


                SettingsSeedBox.Text = seed;

            }



            bool pvpEnabled =
                SettingsPvpCheckBox.IsChecked == true;




            ServerProperties.Set(selectedServer.Path, "view-distance", viewDistance.ToString());

            ServerProperties.Set(selectedServer.Path, "simulation-distance", simDistance.ToString());

            ServerProperties.Set(selectedServer.Path, "max-players", maxPlayers.ToString());

            ServerProperties.Set(selectedServer.Path, "difficulty", difficulty);

            ServerProperties.Set(selectedServer.Path, "online-mode", isPirate ? "false" : "true");

            ServerProperties.Set(selectedServer.Path, "level-seed", seed);

            ServerProperties.Set(selectedServer.Path, "pvp", pvpEnabled ? "true" : "false");



            selectedServer.OnlineMode = !isPirate;


            serverManager.SaveConfig(selectedServer);




            if (consoleManager.IsRunning)
            {

                // сложность можно применить сразу живой командой без
                // перезапуска — остальные параметры требуют рестарта
                consoleManager.SendCommand($"difficulty {difficulty}");



                MessageBox.Show(
                    "Настройки сохранены. Сложность применена сразу, " +
                    "остальные изменения вступят в силу после перезапуска " +
                    "сервера (кнопка \"↻ Перезапуск\").",
                    "Готово"
                );

            }
            else
            {

                MessageBox.Show(
                    "Настройки сохранены и применятся при следующем запуске сервера.",
                    "Готово"
                );

            }

        }








        private void RenameServer_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (selectedServer == null)
            {

                MessageBox.Show(
                    "Сначала выбери сервер"
                );


                return;

            }



            string newName =
                SettingsNameBox.Text.Trim();



            if (string.IsNullOrWhiteSpace(newName))
            {

                MessageBox.Show(
                    "Введите новое название"
                );


                return;

            }



            if (consoleManager.IsRunning)
            {

                MessageBox.Show(
                    "Останови сервер перед переименованием — папка сервера " +
                    "физически переносится, и работающий процесс сломается.",
                    "Сервер запущен"
                );


                return;

            }



            bool success =
                serverManager.RenameServer(selectedServer, newName);



            if (!success)
            {

                MessageBox.Show(
                    "Не удалось переименовать — возможно, сервер с таким " +
                    "названием уже существует.",
                    "Ошибка"
                );


                return;

            }



            LoadServers();



            ServersListBox.SelectedItem =
                selectedServer.Name;



            ServerTitle.Text =
                selectedServer.Name;



            MessageBox.Show(
                "Сервер переименован!"
            );

        }








        // ===================== ВЕРСИИ ЯДЕР =====================


        private async void CoreComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {

            UpdateCoreSupportText();


            await LoadVersionsForSelectedCore();

        }




        // Показывает, что поддерживает выбранное ядро — моды или плагины
        private void UpdateCoreSupportText()
        {

            if (CoreSupportText == null || CoreComboBox.SelectedItem == null)
                return;



            string core =
                ((ComboBoxItem)CoreComboBox.SelectedItem).Content.ToString()!;



            CoreSupportText.Text =
                GetModSupportText(core);

        }




        private string GetModSupportText(string core)
        {

            return core switch
            {

                "Paper" or "Purpur" or "Bukkit" =>
                    "Это ядро поддерживает ПЛАГИНЫ (папка plugins). Моды не работают.",

                "Fabric" or "Forge" or "NeoForge" =>
                    "Это ядро поддерживает МОДЫ (папка mods). Плагины Bukkit/Spigot не работают.",

                "Vanilla" =>
                    "Vanilla не поддерживает ни моды, ни плагины.",

                _ => ""

            };

        }




        // Возвращает имя папки для установки модов/плагинов под ядро,
        // или null, если ядро вообще ничего такого не поддерживает
        private string? GetModsFolderName(string core)
        {

            return core switch
            {

                "Paper" or "Purpur" or "Bukkit" => "plugins",

                "Fabric" or "Forge" or "NeoForge" => "mods",

                _ => null

            };

        }








        private async void RefreshVersions_Click(
            object sender,
            RoutedEventArgs e)
        {

            await LoadVersionsForSelectedCore(forceRefresh: true);

        }








        private async System.Threading.Tasks.Task LoadVersionsForSelectedCore(bool forceRefresh = false)
        {

            // Событие SelectionChanged у CoreComboBox может сработать ещё
            // ВО ВРЕМЯ InitializeComponent() (из-за SelectedIndex="0" в XAML),
            // когда CoreVersionBox ниже по разметке ещё не создан — тогда
            // тут будет null. Просто выходим, реальная загрузка версий
            // произойдёт позже, когда пользователь реально откроет комбобокс.
            if (CoreVersionBox == null || CoreComboBox.SelectedItem == null)
                return;



            // ГЛАВНЫЙ ФИКС: если пользователь быстро переключает ядра,
            // предыдущий незавершённый запрос мог до этого прилететь с
            // ошибкой уже ПОСЛЕ того, как выбрано другое ядро — создавая
            // впечатление, будто ошибка про текущий выбор, хотя она про
            // старый. Отменяем предыдущий запрос и помечаем его результат
            // как неактуальный.
            versionLoadCts?.Cancel();



            CancellationTokenSource cts =
                new CancellationTokenSource();



            versionLoadCts = cts;



            string core =
                ((ComboBoxItem)CoreComboBox.SelectedItem)
                .Content
                .ToString()!;



            string previousText =
                CoreVersionBox.Text;



            RefreshVersionsButton.IsEnabled = false;

            CoreVersionBox.IsEnabled = false;

            RefreshVersionsButton.Content = "⏳";



            try
            {

                List<string> versions =
                    core switch
                    {

                        "Paper" => await coreDownloader.GetPaperVersions(forceRefresh),

                        "Purpur" => await coreDownloader.GetPurpurVersions(forceRefresh),

                        "Fabric" => await fabricDownloader.GetGameVersions(),

                        "Forge" => await forgeDownloader.GetSupportedMinecraftVersions(forceRefresh),

                        "NeoForge" => await neoForgeDownloader.GetSupportedMinecraftVersions(),

                        "Vanilla" => await vanillaDownloader.GetVersions(forceRefresh),

                        "Bukkit" => await bukkitDownloader.GetApproximateSupportedVersions(),

                        _ => new List<string>()

                    };



                // Пока мы ждали ответа, пользователь уже мог переключиться
                // на другое ядро — тогда этот результат больше не актуален,
                // просто тихо его игнорируем
                if (cts.IsCancellationRequested)
                    return;



                CoreVersionBox.ItemsSource = versions;



                // пытаемся сохранить то, что уже было выбрано/введено, если
                // это есть в новом списке — иначе берём самую свежую версию
                if (versions.Contains(previousText))
                {

                    CoreVersionBox.Text = previousText;

                }
                else if (versions.Count > 0)
                {

                    CoreVersionBox.SelectedIndex = 0;

                }

            }
            catch (Exception ex)
            {

                if (cts.IsCancellationRequested)
                    return;



                MessageBox.Show(
                    "Не удалось получить список версий: " + ex.Message +
                    "\n\nМожно ввести версию вручную в поле.",
                    "Ошибка загрузки версий"
                );

            }
            finally
            {

                if (!cts.IsCancellationRequested)
                {

                    RefreshVersionsButton.IsEnabled = true;

                    CoreVersionBox.IsEnabled = true;

                    RefreshVersionsButton.Content = "🔄";

                }

            }

        }




        // ===================== МОДЫ / ПЛАГИНЫ =====================


        private void ModsPlugins_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (selectedServer == null)
            {

                MessageBox.Show("Сначала выбери сервер");


                return;

            }



            string? folderName =
                GetModsFolderName(selectedServer.Core);



            if (folderName == null)
            {

                MessageBox.Show(
                    "Ядро " + selectedServer.Core + " не поддерживает ни моды, " +
                    "ни плагины. Установи Paper/Purpur (плагины) или " +
                    "Fabric/Forge/NeoForge (моды).",
                    "Не поддерживается"
                );


                return;

            }



            string folderPath =
                Path.Combine(selectedServer.Path, folderName);



            Directory.CreateDirectory(folderPath);



            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Java-архив (*.jar)|*.jar",
                Multiselect = true,
                Title = folderName == "plugins"
                    ? "Выбери плагины (.jar)"
                    : "Выбери моды (.jar)"
            };



            if (dialog.ShowDialog() != true)
                return;



            int copied = 0;



            foreach (string file in dialog.FileNames)
            {

                try
                {

                    string dest =
                        Path.Combine(folderPath, Path.GetFileName(file));



                    File.Copy(file, dest, true);


                    copied++;

                }
                catch
                {
                    // пропускаем файл, если он вдруг занят/недоступен — остальные всё равно скопируются
                }

            }



            MessageBox.Show(
                $"Скопировано файлов: {copied} в папку \"{folderName}\".\n\n" +
                "Если сервер уже запущен — перезапусти его, чтобы изменения подхватились."
            );

        }




        // удалить сервер

        private void DeleteServer_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (isTerrariaMode)
            {

                DeleteTerrariaServer_Click(sender, e);


                return;

            }



            if (selectedServer == null)
            {

                MessageBox.Show(
                    "Сначала выбери сервер"
                );


                return;

            }



            if (consoleManager.IsRunning)
            {

                MessageBox.Show(
                    "Сначала останови сервер перед удалением.",
                    "Сервер запущен"
                );


                return;

            }



            MessageBoxResult confirm =
                MessageBox.Show(
                    $"Удалить сервер \"{selectedServer.Name}\" вместе со всем миром, " +
                    "плагинами и настройками? Это действие необратимо!",
                    "Подтверждение удаления",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            try
            {

                serverManager.DeleteServer(selectedServer);



                selectedServer = null;



                LoadServers();



                ServerTitle.Text = "Сервер не выбран";


                ConsoleBox.Text = "";



                MessageBox.Show(
                    "Сервер удалён."
                );

            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.Message, "Ошибка удаления");

            }

        }








        // ===================== ДОСТУП (СЕТЬ) =====================


        private void ToggleHost_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (networkHost != null && networkHost.IsHosting)
            {

                networkHost.StopHosting();


                ToggleHostButton.Content = "Открыть доступ";


                HostStatusText.Text = "Доступ закрыт";


                return;

            }



            if (selectedServer == null)
            {

                MessageBox.Show(
                    "Сначала выбери сервер, к которому даёшь доступ"
                );


                return;

            }



            if (!int.TryParse(HostPortBox.Text, out int port) ||
                port < 1 || port > 65535)
            {

                MessageBox.Show(
                    "Некорректный порт (число от 1 до 65535)"
                );


                return;

            }



            if (string.IsNullOrWhiteSpace(HostPasswordBox.Text))
            {

                MessageBox.Show(
                    "Придумай пароль для друга"
                );


                return;

            }



            // Менеджеры для этого сервера — создаются именно сейчас,
            // привязаны к общему consoleManager, как и остальные окна
            playerManager =
                new PlayerManager(consoleManager, selectedServer.Path);



            whitelistManager =
                new WhitelistManager(consoleManager, selectedServer.Path);



            networkHost =
                new NetworkHost(
                    consoleManager,
                    playerManager,
                    whitelistManager,
                    selectedServer
                );



            try
            {

                networkHost.StartHosting(port, HostPasswordBox.Text);



                ToggleHostButton.Content = "Закрыть доступ";


                HostStatusText.Text =
                    $"Доступ открыт на порту {port} для сервера \"{selectedServer.Name}\"";

            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    "Не удалось открыть порт: " + ex.Message,
                    "Ошибка"
                );

            }

        }








        private async void ConnectToFriend_Click(
            object sender,
            RoutedEventArgs e)
        {

            if (string.IsNullOrWhiteSpace(ConnectHostBox.Text))
            {

                MessageBox.Show(
                    "Введи IP/адрес друга"
                );


                return;

            }



            if (!int.TryParse(ConnectPortBox.Text, out int port))
            {

                MessageBox.Show(
                    "Некорректный порт"
                );


                return;

            }



            NetworkClient client =
                new NetworkClient();



            bool connected =
                await client.ConnectAsync(
                    ConnectHostBox.Text.Trim(),
                    port,
                    ConnectPasswordBox.Text
                );



            if (!connected)
            {

                MessageBox.Show(
                    "Не удалось подключиться — проверь адрес, порт, пароль " +
                    "и включён ли у друга доступ.",
                    "Ошибка подключения"
                );


                return;

            }



            RemoteServerWindow window =
                new RemoteServerWindow(client);


            window.Show();

        }




        // ===================== TERRARIA: УПРАВЛЕНИЕ =====================


        private void StartTerrariaServer_Click(object sender, RoutedEventArgs e)
        {

            if (selectedTerrariaServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



            if (terrariaConsoleManager.IsRunning)
            {
                MessageBox.Show("Сервер уже запущен!");
                return;
            }



            try
            {

                terrariaConsoleManager.StartServer(selectedTerrariaServer);


                selectedTerrariaServer.Start();


                terrariaServerManager.SaveConfig(selectedTerrariaServer);


                runningTerrariaServer = selectedTerrariaServer;


                ServerTitle.Text = selectedTerrariaServer.Name + " | Запущен";


                TerrariaInfoStatusText.Text = "🟡 Запускается...";


                LoadServers();

            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.Message, "Ошибка запуска");

            }

        }




        private void StopTerrariaServer_Click(object sender, RoutedEventArgs e)
        {

            MessageBoxResult confirm =
                MessageBox.Show(
                    "Остановить сервер? Все игроки будут отключены.",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            try
            {

                terrariaConsoleManager.StopServer();


                runningTerrariaServer = null;



                if (selectedTerrariaServer != null)
                {

                    selectedTerrariaServer.Stop();


                    terrariaServerManager.SaveConfig(selectedTerrariaServer);


                    ServerTitle.Text = selectedTerrariaServer.Name + " | Остановлен";


                    TerrariaInfoStatusText.Text = "🔴 Остановлен";

                }



                LoadServers();

            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.Message);

            }

        }




        private void OpenTerrariaFolder_Click(object sender, RoutedEventArgs e)
        {

            if (selectedTerrariaServer == null)
                return;



            // Для tModLoader реальные файлы (exe, моды, мир) лежат в
            // отдельной безопасной папке, а не в server.Path
            string folder =
                GetTerrariaWorldFolder(selectedTerrariaServer);



            Process.Start("explorer.exe", folder);

        }




        private void DeleteTerrariaServer_Click(object sender, RoutedEventArgs e)
        {

            if (selectedTerrariaServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



            if (terrariaConsoleManager.IsRunning)
            {
                MessageBox.Show("Сначала останови сервер перед удалением.", "Сервер запущен");
                return;
            }



            MessageBoxResult confirm =
                MessageBox.Show(
                    $"Удалить сервер \"{selectedTerrariaServer.Name}\" вместе со всем миром и настройками? Это необратимо!",
                    "Подтверждение удаления",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            try
            {

                terrariaServerManager.DeleteServer(selectedTerrariaServer);



                selectedTerrariaServer = null;


                LoadServers();


                ServerTitle.Text = "Сервер не выбран";

                ConsoleBox.Text = "";



                MessageBox.Show("Сервер удалён.");

            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.Message, "Ошибка удаления");

            }

        }




        // ===================== TERRARIA: TSHOCK =====================


        private async void RefreshTShockVersions_Click(object sender, RoutedEventArgs e)
        {

            RefreshTShockVersionsButton.IsEnabled = false;



            try
            {

                var versions = await tshockDownloader.GetVersions();


                TShockVersionBox.ItemsSource = versions;

            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    "Не удалось получить список версий: " + ex.Message +
                    "\n\nМожно оставить поле пустым — тогда установится последний релиз.",
                    "Ошибка загрузки версий"
                );

            }
            finally
            {

                RefreshTShockVersionsButton.IsEnabled = true;

            }

        }




        private async void InstallTShock_Click(object sender, RoutedEventArgs e)
        {

            if (selectedTerrariaServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



            if (terrariaConsoleManager.IsRunning)
            {
                MessageBox.Show("Сначала останови сервер перед сменой движка.");
                return;
            }



            string core =
                ((ComboBoxItem)TerrariaCoreBox.SelectedItem).Content.ToString()!;



            InstallTShockButton.IsEnabled = false;

            InstallTShockButton.Content = "Устанавливаю...";



            InstallProgressWindow progressWindow =
                new InstallProgressWindow($"Установка {core}...")
                {
                    Owner = this
                };


            progressWindow.Show();



            try
            {

                selectedTerrariaServer.ServerCore = core;



                if (core == "tModLoader")
                {

                    progressWindow.AppendLine(
                        "tModLoader ставится через SteamCMD — это не так " +
                        "быстро, как обычная загрузка jar-файла, наберись терпения."
                    );



                    progressWindow.AppendLine(
                        "Ставится в отдельную папку на C:\\ без кириллицы " +
                        "(так требует SteamCMD), не в папку самого проекта."
                    );



                    await tModLoaderDownloader.InstallTModLoader(
                        selectedTerrariaServer.Name,
                        line => progressWindow.AppendLine(line)
                    );



                    selectedTerrariaServer.TShockVersion = "последняя (Steam)";

                }
                else
                {

                    string? version =
                        string.IsNullOrWhiteSpace(TShockVersionBox.Text)
                            ? null
                            : TShockVersionBox.Text.Trim();



                    await tshockDownloader.InstallTShock(
                        selectedTerrariaServer.Path,
                        version,
                        line => progressWindow.AppendLine(line)
                    );



                    selectedTerrariaServer.TShockVersion =
                        version ?? "последняя";

                }



                terrariaServerManager.SaveConfig(selectedTerrariaServer);


                TerrariaInfoVersionBox.Text =
                    selectedTerrariaServer.ServerCore + " (" + selectedTerrariaServer.TShockVersion + ")";



                MessageBox.Show(core + " установлен!");

            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.Message, "Ошибка установки " + core);

            }
            finally
            {

                InstallTShockButton.IsEnabled = true;

                InstallTShockButton.Content = "Установить " + core;


                progressWindow.Close();

            }

        }




        // Меняем подписи/доступность полей в зависимости от выбранного
        // движка — у tModLoader нет отдельного выбора версии через SteamCMD
        // (всегда ставится актуальная сборка), так что поле версии тут не нужно
        private void TerrariaCoreBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

            if (TerrariaCoreBox.SelectedItem == null || InstallTShockButton == null)
                return;



            string core =
                ((ComboBoxItem)TerrariaCoreBox.SelectedItem).Content.ToString()!;



            bool isTModLoader = core == "tModLoader";



            TShockVersionBox.IsEnabled = !isTModLoader;

            RefreshTShockVersionsButton.IsEnabled = !isTModLoader;


            InstallTShockButton.Content = "Установить " + core;

        }




        private void SaveTerrariaSettings_Click(object sender, RoutedEventArgs e)
        {

            if (selectedTerrariaServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



            if (!int.TryParse(TerrariaPortBox.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Некорректный порт");
                return;
            }



            if (!int.TryParse(TerrariaMaxPlayersBox.Text, out int maxPlayers) || maxPlayers < 1)
            {
                MessageBox.Show("Некорректное макс. число игроков");
                return;
            }



            selectedTerrariaServer.Port = port;

            selectedTerrariaServer.MaxPlayers = maxPlayers;

            selectedTerrariaServer.Password = TerrariaPasswordBox.Text;



            if (TerrariaWorldSizeBox.SelectedItem is ComboBoxItem sizeItem)
            {
                selectedTerrariaServer.WorldSize = int.Parse((string)sizeItem.Tag);
            }



            if (TerrariaDifficultyBox.SelectedItem is ComboBoxItem diffItem)
            {
                selectedTerrariaServer.Difficulty = int.Parse((string)diffItem.Tag);
            }



            terrariaServerManager.SaveConfig(selectedTerrariaServer);



            MessageBox.Show(
                "Настройки сохранены. Если мир уже создан — размер/сложность " +
                "на него уже не повлияют, только на новый."
            );

        }




        // ===================== TERRARIA: IP =====================


        private async Task LoadTerrariaPublicIpAsync()
        {

            if (selectedTerrariaServer == null)
                return;



            string ip =
                await NetworkInfo.GetPublicIpAsync();



            TerrariaInfoPublicIpBox.Text =
                $"{ip}:{selectedTerrariaServer.Port}";

        }




        private async void RefreshTerrariaPublicIp_Click(object sender, RoutedEventArgs e)
        {

            TerrariaInfoPublicIpBox.Text = "определяется...";



            await LoadTerrariaPublicIpAsync();

        }




        // ===================== TERRARIA: ИГРОКИ =====================


        private void TerrariaKick_Click(object sender, RoutedEventArgs e)
        {

            if (TerrariaPlayersListBox.SelectedItem == null)
            {
                MessageBox.Show("Сначала выбери игрока из списка.", "Игрок не выбран");
                return;
            }



            terrariaPlayerManager.Kick(TerrariaPlayersListBox.SelectedItem.ToString()!);

        }




        private void TerrariaBan_Click(object sender, RoutedEventArgs e)
        {

            if (TerrariaPlayersListBox.SelectedItem == null)
            {
                MessageBox.Show("Сначала выбери игрока из списка.", "Игрок не выбран");
                return;
            }



            string player =
                TerrariaPlayersListBox.SelectedItem.ToString()!;



            MessageBoxResult confirm =
                MessageBox.Show(
                    $"Забанить {player}?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            terrariaPlayerManager.Ban(player);

        }




        // ===================== TERRARIA: МИР =====================


        // Для tModLoader мир лежит в отдельной безопасной ASCII-папке
        // (см. TModLoaderPaths), для TShock — как обычно, в server.Path
        private string GetTerrariaWorldFolder(TerrariaServer server)
        {

            return server.ServerCore.Equals("tModLoader", StringComparison.OrdinalIgnoreCase)
                ? TModLoaderPaths.GetServerInstallDir(server.Name)
                : server.Path;

        }




        private void DownloadTerrariaWorld_Click(object sender, RoutedEventArgs e)
        {

            if (selectedTerrariaServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



            if (terrariaConsoleManager.IsRunning)
            {

                MessageBox.Show(
                    "Сначала останови сервер — файл мира может быть занят.",
                    "Сервер запущен"
                );


                return;

            }



            string worldFile =
                Path.Combine(
                    GetTerrariaWorldFolder(selectedTerrariaServer),
                    selectedTerrariaServer.WorldName + ".wld"
                );



            if (!File.Exists(worldFile))
            {

                MessageBox.Show(
                    "Файл мира ещё не создан — запусти сервер хотя бы раз.",
                    "Мир не найден"
                );


                return;

            }



            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "Мир Terraria (*.wld)|*.wld",
                FileName = selectedTerrariaServer.WorldName + ".wld"
            };



            if (dialog.ShowDialog() != true)
                return;



            try
            {

                File.Copy(worldFile, dialog.FileName, true);



                MessageBox.Show("Мир сохранён в " + dialog.FileName);

            }
            catch (Exception ex)
            {

                MessageBox.Show("Не удалось сохранить мир: " + ex.Message, "Ошибка");

            }

        }




        private void UploadTerrariaWorld_Click(object sender, RoutedEventArgs e)
        {

            if (selectedTerrariaServer == null)
            {
                MessageBox.Show("Сначала выбери сервер");
                return;
            }



            if (terrariaConsoleManager.IsRunning)
            {

                MessageBox.Show(
                    "Сначала останови сервер перед загрузкой другого мира.",
                    "Сервер запущен"
                );


                return;

            }



            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Мир Terraria (*.wld)|*.wld"
            };



            if (dialog.ShowDialog() != true)
                return;



            MessageBoxResult confirm =
                MessageBox.Show(
                    "Текущий мир будет ПОЛНОСТЬЮ заменён загруженным. Это необратимо. Продолжить?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );



            if (confirm != MessageBoxResult.Yes)
                return;



            string worldFile =
                Path.Combine(
                    GetTerrariaWorldFolder(selectedTerrariaServer),
                    selectedTerrariaServer.WorldName + ".wld"
                );



            try
            {

                File.Copy(dialog.FileName, worldFile, true);



                MessageBox.Show("Мир загружен и заменён.");

            }
            catch (Exception ex)
            {

                MessageBox.Show("Не удалось загрузить мир: " + ex.Message, "Ошибка");

            }

        }
        // ===================== ТЕМА ОФОРМЛЕНИЯ =====================
        // Переключает светлую/тёмную палитру и запоминает выбор.
        // Логику серверов не затрагивает.

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Toggle();

            UpdateThemeButton();
        }


        private void UpdateThemeButton()
        {
            if (ThemeToggleButton == null)
                return;

            ThemeToggleButton.Content =
                ThemeManager.IsDark ? "☀ Светлая тема" : "🌙 Тёмная тема";
        }

    }
}