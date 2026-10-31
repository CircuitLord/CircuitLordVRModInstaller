using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public IReadOnlyList<PopulationRegion> Regions { get; }

        public ServerPopulation(IEnumerable<PopulationRegion> regions, DateTimeOffset retrievedAt)
        {
            var list = regions.ToList();
            Players = list.Sum(region => region.Players);
            Servers = list.Sum(region => region.Servers);
            RetrievedAt = retrievedAt;
            Regions = list.OrderBy(region => region.Name == "Unknown")
                .ThenByDescending(region => region.Players)
                .ThenBy(region => region.Name, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
        }
    }

    public sealed class PopulationRegion
    {
        public string Name { get; }
        public long Players { get; }
        public int Servers { get; }
        public string Summary => $"{Players:N0} {(Players == 1 ? "player" : "players")} / {Servers:N0} {(Servers == 1 ? "server" : "servers")}";

        public PopulationRegion(string name, long players, int servers)
        {
            Name = name;
            Players = players;
            Servers = servers;
        }
    }

    public static class NorthstarPopulationClient
    {
        public const string ServersUrl = "https://northstar.tf/client/servers";
        static readonly HttpClient Http = CreateHttp();

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

        public static async Task<ServerPopulation> Fetch(CancellationToken cancellationToken)
        {
            using (var response = await Http.GetAsync(ServersUrl, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                // DataContractJsonSerializer also accepts {} as an empty array.
                if (!json.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    throw new InvalidDataException("Northstar returned an invalid server list.");
                var servers = JsonUtil.Deserialize<Server[]>(json);
                if (servers == null) throw new InvalidDataException("Northstar returned no server list.");

                foreach (var server in servers)
                {
                    if (server == null || !server.PlayerCount.HasValue || server.PlayerCount < 0)
                        throw new InvalidDataException("Northstar returned an invalid player count.");
                }
                var regions = servers.GroupBy(server => GroupRegion(server.Region), StringComparer.OrdinalIgnoreCase)
                    .Select(group => new PopulationRegion(group.Key, group.Sum(server => (long)server.PlayerCount.Value), group.Count()));
                return new ServerPopulation(regions, DateTimeOffset.UtcNow);
            }
        }

        static string GroupRegion(string region)
        {
            region = string.IsNullOrWhiteSpace(region) ? "Unknown" : region.Trim();
            foreach (var group in new[] { "US", "EU", "Asia" })
                if (region.Equals(group, StringComparison.OrdinalIgnoreCase) || region.StartsWith(group + " ", StringComparison.OrdinalIgnoreCase))
                    return group;
            return region.Equals("AUS", StringComparison.OrdinalIgnoreCase) ? "Australia" : region;
        }

        [DataContract]
        sealed class Server
        {
            [DataMember(Name = "playerCount", IsRequired = true)]
            public int? PlayerCount { get; set; }
            [DataMember(Name = "region")]
            public string Region { get; set; }
        }
    }
}
