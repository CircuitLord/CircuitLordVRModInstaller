using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BigWalkVRInstaller.Services
{
    public sealed class ServerPopulation
    {
        public long Players { get; }
        public int Servers { get; }
        public DateTimeOffset RetrievedAt { get; }

        public ServerPopulation(long players, int servers, DateTimeOffset retrievedAt)
        {
            Players = players;
            Servers = servers;
            RetrievedAt = retrievedAt;
        }
    }

    public sealed class NorthstarPopulationClient
    {
        public const string ServersUrl = "https://northstar.tf/client/servers";
        static readonly HttpClient SharedHttp = CreateHttp();
        readonly HttpClient _http;

        public NorthstarPopulationClient() : this(SharedHttp) { }

        // The caller owns an injected client; the default client is shared for the app lifetime.
        public NorthstarPopulationClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

        static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = 4 * 1024 * 1024
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CircuitLordVRModInstaller");
            return http;
        }

        public async Task<ServerPopulation> Fetch(CancellationToken cancellationToken)
        {
            using (var response = await _http.GetAsync(ServersUrl, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                // DataContractJsonSerializer also accepts {} as an empty array.
                if (!json.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    throw new InvalidDataException("Northstar returned an invalid server list.");
                var servers = JsonUtil.Deserialize<Server[]>(json);
                if (servers == null) throw new InvalidDataException("Northstar returned no server list.");

                long players = 0;
                foreach (var server in servers)
                {
                    if (server == null || !server.PlayerCount.HasValue || server.PlayerCount < 0)
                        throw new InvalidDataException("Northstar returned an invalid player count.");
                    players += server.PlayerCount.Value;
                }
                return new ServerPopulation(players, servers.Length, DateTimeOffset.UtcNow);
            }
        }

        [DataContract]
        sealed class Server
        {
            [DataMember(Name = "playerCount", IsRequired = true)]
            public int? PlayerCount { get; set; }
        }
    }
}
