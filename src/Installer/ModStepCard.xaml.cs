using System;
using System.Windows;
using System.Windows.Controls;

namespace BigWalkVRInstaller
{
    public partial class ModStepCard : UserControl
    {
        public static readonly DependencyProperty StepProperty = Register(nameof(Step), typeof(int), 1);
        public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), null);
        public static readonly DependencyProperty DetailProperty = Register(nameof(Detail), typeof(string), null);
        // null hides the tag
        public static readonly DependencyProperty ChannelTagProperty = Register(nameof(ChannelTag), typeof(string), null);
        public static readonly DependencyProperty DoneProperty = Register(nameof(Done), typeof(bool), false);
        public static readonly DependencyProperty StateProperty = Register(nameof(State), typeof(InstallState), InstallState.Install);
        public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), null);
        public static readonly DependencyProperty ShowChannelsProperty = Register(nameof(ShowChannels), typeof(bool), false);
        public static readonly DependencyProperty ActionEnabledProperty = Register(nameof(ActionEnabled), typeof(bool), true);
        public static readonly DependencyProperty ShowUninstallProperty = Register(nameof(ShowUninstall), typeof(bool), false);
        // busy hides the actions, progress shows only while something is downloading or installing
        public static readonly DependencyProperty BusyProperty = Register(nameof(Busy), typeof(bool), false);
        public static readonly DependencyProperty ShowProgressProperty = Register(nameof(ShowProgress), typeof(bool), false);
        public static readonly DependencyProperty BusyTextProperty = Register(nameof(BusyText), typeof(string), null);
        public static readonly DependencyProperty ProgressProperty = Register(nameof(Progress), typeof(double), 0d);

        public static readonly RoutedEvent UninstallClickEvent = EventManager.RegisterRoutedEvent(
            nameof(UninstallClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ModStepCard));

        public int Step { get => (int)GetValue(StepProperty); set => SetValue(StepProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
        public string ChannelTag { get => (string)GetValue(ChannelTagProperty); set => SetValue(ChannelTagProperty, value); }
        public bool Done { get => (bool)GetValue(DoneProperty); set => SetValue(DoneProperty, value); }
        public InstallState State { get => (InstallState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public bool ShowChannels { get => (bool)GetValue(ShowChannelsProperty); set => SetValue(ShowChannelsProperty, value); }
        public bool ActionEnabled { get => (bool)GetValue(ActionEnabledProperty); set => SetValue(ActionEnabledProperty, value); }
        public bool ShowUninstall { get => (bool)GetValue(ShowUninstallProperty); set => SetValue(ShowUninstallProperty, value); }
        public bool Busy { get => (bool)GetValue(BusyProperty); set => SetValue(BusyProperty, value); }
        public bool ShowProgress { get => (bool)GetValue(ShowProgressProperty); set => SetValue(ShowProgressProperty, value); }
        public string BusyText { get => (string)GetValue(BusyTextProperty); set => SetValue(BusyTextProperty, value); }
        public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

        // the install action's events bubble out of the card, OriginalSource is the action
        public event RoutedEventHandler Click { add => AddHandler(InstallAction.ClickEvent, value); remove => RemoveHandler(InstallAction.ClickEvent, value); }
        public event RoutedEventHandler ChannelsClick { add => AddHandler(InstallAction.ChannelsClickEvent, value); remove => RemoveHandler(InstallAction.ChannelsClickEvent, value); }
        public event RoutedEventHandler UninstallClick { add => AddHandler(UninstallClickEvent, value); remove => RemoveHandler(UninstallClickEvent, value); }

        public ModStepCard()
        {
            InitializeComponent();
            Apply();
        }

        static DependencyProperty Register(string name, Type type, object fallback) =>
            DependencyProperty.Register(name, type, typeof(ModStepCard), new PropertyMetadata(fallback, (d, e) => ((ModStepCard)d).Apply()));

        void Apply()
        {
            BadgeText.Tag = Step.ToString();
            StepBadge.Set(Badge, BadgeText, Done);
            TitleText.Text = Title;
            DetailText.Text = Detail;
            ChannelTagText.Text = ChannelTag;
            ChannelTagBorder.Visibility = ChannelTag != null ? Visibility.Visible : Visibility.Collapsed;
            UninstallButton.Visibility = ShowUninstall && !Busy ? Visibility.Visible : Visibility.Collapsed;
            Action.Visibility = Busy ? Visibility.Collapsed : Visibility.Visible;
            Action.State = State;
            Action.Label = Label;
            Action.ShowChannels = ShowChannels;
            Action.ActionEnabled = ActionEnabled;
            ProgressPanel.Visibility = ShowProgress ? Visibility.Visible : Visibility.Collapsed;
            ProgressText.Text = BusyText;
            ProgressBar.Value = Progress;
        }

        void Uninstall_Click(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(UninstallClickEvent, this));
    }
}
