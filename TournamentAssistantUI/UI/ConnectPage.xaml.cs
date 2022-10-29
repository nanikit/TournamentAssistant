using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using TournamentAssistantUI.Misc;

namespace TournamentAssistantUI.UI
{
    /// <summary>
    /// Interaction logic for ConnectPage.xaml
    /// </summary>
    public partial class ConnectPage : Page
    {
        public ConnectPage()
        {
            InitializeComponent();

#if DEBUG
            WinConsole.Initialize();
#else
            MockButton.Visibility = Visibility.Hidden;
#endif
            HostIP.Text = $"localhost:2052";
            Loaded += ConnectToLocalhost;
        }

        private async void ConnectToLocalhost(object sender, RoutedEventArgs e)
        {
            await WaitPortOpen();
            Connect_Click(this, new RoutedEventArgs());
            var navigator = NavigationService;
            navigator.Navigated += (s, ev) =>
            {
                if (ev.Content is not MainPage main)
                {
                    return;
                }

                main.Players.CollectionChanged += (object _source, NotifyCollectionChangedEventArgs ev) =>
                {
                    if (ev.Action == NotifyCollectionChangedAction.Add)
                    {
                        CreateTestMatch(navigator, main);
                    }
                };
            };
        }

        private void CreateTestMatch(NavigationService navigator, MainPage main)
        {
            main.PlayerListBox.SelectAll();
            main.CreateMatch.Execute(null);

            async void SetupTest(object _source, NavigationEventArgs ev)
            {
                if (ev.Content is MatchPage match)
                {
                    match.SongUrlBox.Text = "234c9";
                    match.LoadSong.Execute(this);

                    var taskSource = new TaskCompletionSource<bool>();
                    void WaitLoad(object sender, PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName != nameof(match.Match))
                        {
                            return;
                        }
                        bool hasDifficulties = match.Match?.SelectedLevel?.Characteristics?.Any(x => x.Difficulties.Length > 0) ?? false;
                        if (!hasDifficulties)
                        {
                            return;
                        }
                        taskSource.TrySetResult(true);
                        match.PropertyChanged -= WaitLoad;
                    }
                    match.PropertyChanged += WaitLoad;
                    await taskSource.Task;

                    match.CharacteristicDropdown.SelectedIndex = 0;
                    match.DifficultyDropdown.SelectedIndex = 1;
                    navigator.Navigated -= SetupTest;
                }
            }
            navigator.Navigated += SetupTest;
        }

        private static async Task WaitPortOpen()
        {
            var client = new TcpClient();
            while (true)
            {
                try
                {
                    await client.ConnectAsync("localhost", 2052).ConfigureAwait(false);
                    client.Close();
                    break;
                }
                catch (Exception)
                {
                    // ignore
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        private void Mock_Click(object sender, RoutedEventArgs e)
        {
            var navigationService = NavigationService.GetNavigationService(this);
            navigationService.Navigate(new MockPage());
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            var hostText = HostIP.Text.Split(':');

            var navigationService = NavigationService.GetNavigationService(this);
            navigationService.Navigate(new MainPage(hostText[0], hostText.Length > 1 ? int.Parse(hostText[1]) : 2052, string.IsNullOrEmpty(Username.Text) ? "Coordinator" : Username.Text, Password.Text));
        }
    }
}
