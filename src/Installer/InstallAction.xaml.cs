using System;
using System.Windows;
using System.Windows.Controls;

namespace BigWalkVRInstaller
{
    public partial class InstallAction : UserControl
    {
        public static readonly DependencyProperty StateProperty = Register(nameof(State), typeof(InstallState), InstallState.Install);
        public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), null);
        public static readonly DependencyProperty ShowChannelsProperty = Register(nameof(ShowChannels), typeof(bool), false);
        public static readonly DependencyProperty ActionEnabledProperty = Register(nameof(ActionEnabled), typeof(bool), true);

        public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
            nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InstallAction));
        public static readonly RoutedEvent ChannelsClickEvent = EventManager.RegisterRoutedEvent(
            nameof(ChannelsClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InstallAction));

        public InstallState State { get => (InstallState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public bool ShowChannels { get => (bool)GetValue(ShowChannelsProperty); set => SetValue(ShowChannelsProperty, value); }
        public bool ActionEnabled { get => (bool)GetValue(ActionEnabledProperty); set => SetValue(ActionEnabledProperty, value); }

        public event RoutedEventHandler Click { add => AddHandler(ClickEvent, value); remove => RemoveHandler(ClickEvent, value); }
        public event RoutedEventHandler ChannelsClick { add => AddHandler(ChannelsClickEvent, value); remove => RemoveHandler(ChannelsClickEvent, value); }

        public InstallAction()
        {
            InitializeComponent();
            Apply();
        }

        static DependencyProperty Register(string name, Type type, object fallback) =>
            DependencyProperty.Register(name, type, typeof(InstallAction), new PropertyMetadata(fallback, (d, e) => ((InstallAction)d).Apply()));

        void Apply()
        {
            var installed = State == InstallState.Installed;
            // the joined edge squares off when the dropdown is shown
            var corners = ShowChannels ? new CornerRadius(6, 0, 0, 6) : new CornerRadius(6);
            ActionButton.Content = Label;
            ActionButton.IsEnabled = ActionEnabled;
            ActionButton.Style = (Style)FindResource(State == InstallState.Update ? "Update" : "Primary");
            ActionButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            InstalledChip.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            ChannelsButton.Style = (Style)FindResource(State == InstallState.Install ? "ChannelsButton" : installed ? "ChannelsInstalled" : "ChannelsUpdate");
            ChannelsButton.Visibility = ShowChannels ? Visibility.Visible : Visibility.Collapsed;
            Corners.SetRadius(ActionButton, corners);
            Corners.SetRadius(InstalledChip, corners);
        }

        void Action_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        void Channels_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(ChannelsClickEvent, this));
    }
}
