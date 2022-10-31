﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using TournamentAssistantUI.UI.UserControls;

namespace TournamentAssistantUI.UI
{
    /// <summary>
    /// Interaction logic for MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        public ICommand CreateMatch { get; }
        public ICommand AddAllPlayersToMatch { get; }
        public ICommand DestroyMatch { get; }

        public SystemClient Client { get; }

        public User[] PlayersNotInMatch
        {
            get
            {
                List<User> playersInMatch = new List<User>();
                foreach (var match in Client.State.Matches)
                {
                    playersInMatch.AddRange(Client.State.Users.Where(x => match.AssociatedUsers.Contains(x.Guid) && x.ClientType == User.ClientTypes.Player));
                }
                return Client.State.Users.Where(x => x.ClientType == User.ClientTypes.Player).Except(playersInMatch).ToArray();
            }
        }

        public ObservableCollection<User> Players { get; private set; } = new();

        public MainPage(string endpoint = null, int port = 2052, string username = null, string password = null)
        {
            InitializeComponent();

            DataContext = this;

            CreateMatch = new CommandImplementation(CreateMatch_Executed, CreateMatch_CanExecute);
            AddAllPlayersToMatch = new CommandImplementation(AddAllPlayersToMatch_Executed, AddAllPlayersToMatch_CanExecute);
            DestroyMatch = new CommandImplementation(DestroyMatch_Executed, (_) => true);

            Client = new SystemClient(endpoint, port, username, User.ClientTypes.Coordinator, password: password);

            //As of the async refactoring, this *shouldn't* cause problems to not await. It would be very hard to properly use async from a UI event so I'm leaving it like this for now
            Task.Run(Client.Start);

            //This marks the death of me trying to do WPF correctly. This became necessary after the switch to protobuf, when NotifyUpdate stopped having an effect on certain ui elements
            Client.MatchCreated += Client_MatchChanged;
            Client.MatchInfoUpdated += Client_MatchChanged;
            Client.MatchDeleted += Client_MatchChanged;

            Client.UserConnected += Client_UserConnected;
            Client.UserInfoUpdated += Client_PlayerChanged;
            Client.UserDisconnected += Client_UserDisconnected;

            Client.ConnectedToServer += Client_ConnectedToServer;

            RefreshUserBoxes();
        }

        private Task Client_UserConnected(User user)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                switch (user.ClientType)
                {
                    case User.ClientTypes.Player:
                        Players.Add(user);
                        PlayerCountText.Text = $"Player Waiting Room ({Players.Count})";
                        break;
                    case User.ClientTypes.Coordinator:
                        CoordinatorListBox.Items.Add(user);
                        break;
                }
            }).Task;
        }

        private Task Client_PlayerChanged(User user)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                var player = Players
                    .Select((user, index) => (User: user, Index: index))
                    .FirstOrDefault(x => x.User.Guid == user.Guid);
                if (player is var (item, index))
                {
                    Players[index] = item;
                }
            }).Task;
        }

        private Task Client_UserDisconnected(User user)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                var player = Players
                    .Select((user, index) => (User: user, Index: index))
                    .FirstOrDefault(x => x.User.Guid == user.Guid);
                if (player is var (item, index) && item != null)
                {
                    Players.RemoveAt(index);
                    PlayerCountText.Text = $"Player Waiting Room ({Players.Count})";
                }
            }).Task;
        }

        private Task Client_ConnectedToServer(Response.Connect _)
        {
            RefreshUserBoxes();
            return Task.CompletedTask;
        }

        private Task Client_MatchChanged(Match _)
        {
            Dispatcher.Invoke(MatchListBox.Items.Refresh);
            return Task.CompletedTask;
        }

        private void RefreshUserBoxes()
        {
            //I've given up on bindnigs now that I need to filter a user list for each box. We're doing this instead since WPF was supposed to be a temporary solution anyway
            Dispatcher.Invoke(() =>
            {
                Players.Clear();
                CoordinatorListBox.Items.Clear();

                if (Client?.State?.Users != null)
                {
                    foreach (var player in Client.State.Users.Where(x => x.ClientType == User.ClientTypes.Player))
                    {
                        Players.Add(player);
                    }
                    foreach (var coordinator in Client.State.Users.Where(x => x.ClientType == User.ClientTypes.Coordinator))
                    {
                        CoordinatorListBox.Items.Add(coordinator);
                    }

                    PlayerCountText.Text = $"Player Waiting Room ({Players.Count})";
                }
            });
        }

        private void DestroyMatch_Executed(object obj)
        {
            //As of the async refactoring, this *shouldn't* cause problems to not await. It would be very hard to properly use async from a UI event so I'm leaving it like this for now
            Task.Run(() => Client.DeleteMatch(obj as Match));
        }

        private void CreateMatch_Executed(object o)
        {
            var players = PlayerListBox.SelectedItems.Cast<User>();
            var match = new Match()
            {
                Guid = Guid.NewGuid().ToString()
            };
            match.AssociatedUsers.AddRange(players.Select(x => x.Guid));
            match.AssociatedUsers.Add(Client.Self.Guid);
            match.Leader = Client.Self.Guid;

            //As of the async refactoring, this *shouldn't* cause problems to not await. It would be very hard to properly use async from a UI event so I'm leaving it like this for now
            Task.Run(() => Client.CreateMatch(match));
            NavigateToMatchPage(match);
        }

        private bool CreateMatch_CanExecute(object o)
        {
            //return PlayerListBox.SelectedItems.Count > 1;
            return PlayerListBox.SelectedItems.Count > 0;
            //return true;
        }

        private void AddAllPlayersToMatch_Executed(object o)
        {
            var players = PlayerListBox.Items.Cast<User>();
            var match = new Match()
            {
                Guid = Guid.NewGuid().ToString(),
                Leader = Client.Self.Guid
            };
            match.AssociatedUsers.AddRange(players.Select(x => x.Guid));
            match.AssociatedUsers.Add(Client.Self.Guid);

            //As of the async refactoring, this *shouldn't* cause problems to not await. It would be very hard to properly use async from a UI event so I'm leaving it like this for now
            Task.Run(() => Client.CreateMatch(match));
            NavigateToMatchPage(match);
        }

        private bool AddAllPlayersToMatch_CanExecute(object o)
        {
            return PlayerListBox.Items.Count > 0;
        }

        private void MatchListItemGrid_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var matchItem = (sender as MatchItem);
            NavigateToMatchPage(matchItem.Match);
        }

        private async void NavigateToMatchPage(Match match)
        {
            if (!IsLoaded)
            {
                var taskSource = new TaskCompletionSource<bool>();
                void WaitLoad(object _source, RoutedEventArgs ev)
                {
                    taskSource.TrySetResult(true);
                    Loaded -= WaitLoad;
                }
                Loaded += WaitLoad;
                await taskSource.Task;
            }
            NavigationService?.Navigate(new MatchPage(match, this));
        }

        private void MatchListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViwer = GetScrollViewer(sender as DependencyObject) as ScrollViewer;
            if (scrollViwer != null)
            {
                if (e.Delta < 0)
                {
                    scrollViwer.ScrollToVerticalOffset(scrollViwer.VerticalOffset + 15);
                }
                else if (e.Delta > 0)
                {
                    scrollViwer.ScrollToVerticalOffset(scrollViwer.VerticalOffset - 15);
                }
            }
        }

        private static DependencyObject GetScrollViewer(DependencyObject o)
        {
            if (o is ScrollViewer)
            { return o; }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);

                var result = GetScrollViewer(child);
                if (result == null)
                {
                    continue;
                }
                else
                {
                    return result;
                }
            }
            return null;
        }
    }
}
