using System;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using BigWalkVRInstaller;
using BigWalkVRInstaller.Services;

namespace InstallerValidation
{
    static class PopulationValidation
    {
        sealed class Handler : HttpMessageHandler
        {
            public Func<CancellationToken, Task<HttpResponseMessage>> Respond;
            public int Requests;
            public CancellationToken Token;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Check(request.Method == HttpMethod.Get && request.RequestUri.AbsoluteUri == NorthstarPopulationClient.ServersUrl,
                    "population requested the wrong endpoint");
                Requests++;
                Token = token;
                return Respond(token);
            }
        }

        static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new HttpResponseMessage(status) { Content = new StringContent(json) };

        public static int Run()
        {
            try
            {
                Validate();
                Console.WriteLine("Population validation passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        public static void Validate()
        {
            var handler = new Handler();
            using (var http = new HttpClient(handler))
            {
                var client = new NorthstarPopulationClient(http);
                Func<string, ServerPopulation> fetch = json =>
                {
                    handler.Respond = token => Task.FromResult(Response(json));
                    return client.Fetch(CancellationToken.None).GetAwaiter().GetResult();
                };
                var before = DateTimeOffset.UtcNow;
                var counts = fetch("[{\"playerCount\":12,\"maxPlayers\":16,\"name\":\"One\"},{\"playerCount\":0},{\"playerCount\":3}]");
                Check(counts.Players == 15 && counts.Servers == 3, "population totals were incorrect");
                Check(counts.RetrievedAt >= before && counts.RetrievedAt <= DateTimeOffset.UtcNow, "population timestamp was incorrect");
                counts = fetch("["
                    + "{\"name\":\"Quiet US\",\"region\":\"US East\",\"playerCount\":0,\"maxPlayers\":16},"
                    + "{\"name\":\"Busy US\",\"region\":\"US West\",\"playerCount\":12,\"maxPlayers\":16},"
                    + "{\"name\":\"EU match\",\"region\":\"EU North\",\"playerCount\":3,\"maxPlayers\":12},"
                    + "{\"name\":\"Asia match\",\"region\":\"Asia East\",\"playerCount\":1},"
                    + "{\"region\":\"AUS\",\"playerCount\":0},"
                    + "{\"region\":\"  \",\"playerCount\":20}]");
                Check(counts.Regions.Select(region => region.Name).SequenceEqual(new[] { "US", "EU", "Asia", "Australia", "Unknown" }), "servers were not grouped by reported region");
                var us = counts.Regions[0];
                Check(us.Players == 12 && us.Servers.Count == 2 && us.Servers[0].Name == "Busy US", "region totals or server ordering were incorrect");
                Check(us.Servers[0].Region == "US West" && us.Servers[0].PlayerCountText == "12 / 16", "server location or slots were lost");
                Check(counts.Regions.Last().Servers[0].Name == "Unnamed server" && counts.Regions.Last().Servers[0].Region == "Unknown", "missing server metadata was guessed");
                counts = fetch("[{\"name\":\"^aabbccddColored server\",\"playerCount\":1}]");
                Check(counts.Regions[0].Servers[0].Name == "Colored server", "Northstar color codes obscured the server name");
                counts = fetch("[]");
                Check(counts.Players == 0 && counts.Servers == 0, "empty list was not a valid zero population");
                counts = fetch("[{\"playerCount\":2147483647},{\"playerCount\":1}]");
                Check(counts.Players == 2147483648L, "population total overflowed");
                foreach (var json in new[] { "null", "{}", "[null]", "[{}]", "[{\"playerCount\":-1}]", "[{\"playerCount\":null}]", "[{\"playerCount\":1,\"maxPlayers\":-1}]", "not json" })
                    Reject(() => fetch(json), "invalid population was accepted: " + json);

                handler.Respond = token => Task.FromResult(Response("[]", HttpStatusCode.ServiceUnavailable));
                Reject(() => client.Fetch(CancellationToken.None).GetAwaiter().GetResult(), "HTTP failure was treated as zero players");
                handler.Respond = token => Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline"));
                Reject(() => client.Fetch(CancellationToken.None).GetAwaiter().GetResult(), "connection failure was treated as zero players");
                using (var cancel = new CancellationTokenSource())
                {
                    handler.Respond = async token =>
                    {
                        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                        return Response("[]");
                    };
                    var pending = client.Fetch(cancel.Token);
                    cancel.Cancel();
                    try { pending.GetAwaiter().GetResult(); throw new Exception("cancellation was ignored"); }
                    catch (OperationCanceledException) { }
                }

                ValidateWindow(client, handler);
            }
        }

        static void ValidateWindow(NorthstarPopulationClient client, Handler handler)
        {
            var app = (App)System.Windows.Application.Current ?? new App();
            app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            if (app.Resources.Count == 0) app.InitializeComponent();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var window = new MainWindow();
            Field(window, "_populationClient").SetValue(window, client);
            var sessionField = Field(window, "_populationSession");
            var text = (TextBlock)window.FindName("PopulationText");
            var updated = (TextBlock)window.FindName("PopulationUpdatedText");
            var refresh = (Button)window.FindName("RefreshPopulationButton");
            var players = (Button)window.FindName("PlayersButton");
            var overlay = (System.Windows.Controls.Border)window.FindName("PopulationOverlay");
            var regions = (ItemsControl)window.FindName("PopulationRegionsList");
            Func<Task> refreshCounts = () => (Task)Method("RefreshServerPopulation").Invoke(window, null);
            try
            {
                Check(overlay.Visibility == System.Windows.Visibility.Collapsed, "population details were open by default");
                players.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                Check(overlay.Visibility == System.Windows.Visibility.Visible && !((Grid)window.FindName("InstallerView")).IsEnabled, "Players button did not open modal details");
                ((Button)window.FindName("ClosePopulationButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                Check(overlay.Visibility == System.Windows.Visibility.Collapsed && ((Grid)window.FindName("InstallerView")).IsEnabled, "closing player details did not restore setup controls");
                sessionField.SetValue(window, new CancellationTokenSource());
                handler.Respond = token => Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline"));
                Wait(refreshCounts());
                Check(text.Text == "Player counts unavailable" && refresh.IsEnabled, "initial offline state did not offer retry");
                handler.Respond = token => Task.FromResult(Response("[{\"playerCount\":1}]"));
                Wait(refreshCounts());
                Check(text.Text == "1 player online across 1 server" && updated.Text.StartsWith("Updated "), "population UI did not recover");
                Check((string)players.Content == "Players (1)" && regions.Items.Count == 1, "compact button or region list did not update");
                ((PopulationRegion)regions.Items[0]).IsExpanded = true;
                Wait(refreshCounts());
                Check(((PopulationRegion)regions.Items[0]).IsExpanded, "refresh collapsed the region being browsed");
                handler.Respond = token => Task.FromResult(Response("[]", HttpStatusCode.BadGateway));
                Wait(refreshCounts());
                Check(text.Text.EndsWith("(last known)") && updated.Text.Contains("Last updated"), "stale population was not labeled");
                Check((string)players.Content == "Players (1*)" && regions.Items.Count == 1, "offline refresh lost the last known server list");
                var completion = new TaskCompletionSource<HttpResponseMessage>();
                handler.Respond = token => completion.Task;
                var pending = refreshCounts();
                var requests = handler.Requests;
                Check(!refresh.IsEnabled && updated.Text == "Refreshing...", "loading state missing");
                Wait(refreshCounts());
                Check(handler.Requests == requests, "overlapping population request was started");
                var token = handler.Token;
                Method("StopPopulationUpdates").Invoke(window, null);
                Check(token.IsCancellationRequested && sessionField.GetValue(window) == null, "leaving screen did not cancel population request");
                sessionField.SetValue(window, new CancellationTokenSource());
                handler.Respond = ct => Task.FromResult(Response("[]"));
                Wait(refreshCounts());
                completion.SetResult(Response("[{\"playerCount\":99}]"));
                Wait(pending);
                Check(text.Text == "0 players online across 0 servers" && refresh.IsEnabled, "old request overwrote the new screen session");
                Check(regions.Items.Count == 0 && ((TextBlock)window.FindName("PopulationEmptyText")).Visibility == System.Windows.Visibility.Visible, "empty server list was not explained");
                var closingSession = (CancellationTokenSource)sessionField.GetValue(window);
                var closingToken = closingSession.Token;
                window.Close();
                Check(closingToken.IsCancellationRequested, "closing window did not cancel population updates");
            }
            finally
            {
                Method("StopPopulationUpdates").Invoke(window, null);
                window.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        static void Wait(Task task)
        {
            if (!task.IsCompleted)
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }

        static FieldInfo Field(MainWindow window, string name) => window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo Method(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception) { return; }
            throw new Exception(message);
        }
    }
}
