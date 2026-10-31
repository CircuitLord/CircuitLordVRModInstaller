using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
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

        public ServerPopulation(IEnumerable<PopulationServer> servers, DateTimeOffset retrievedAt)
        {
            var list = servers.ToList();
            Players = list.Sum(server => (long)server.Players);
            Servers = list.Count;
            RetrievedAt = retrievedAt;
            Regions = list.GroupBy(server => server.RegionGroup, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PopulationRegion(group.Key, group))
                .OrderBy(region => region.Name == "Unknown")
                .ThenByDescending(region => region.Players)
                .ThenBy(region => region.Name, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
        }
    }

    public sealed class PopulationRegion
    {
        public string Name { get; }
        public bool IsExpanded { get; set; }
        public long Players { get; }
        public IReadOnlyList<PopulationServer> Servers { get; }
        public string Summary => $"{Players:N0} {(Players == 1 ? "player" : "players")} / {Servers.Count:N0} {(Servers.Count == 1 ? "server" : "servers")}";

        public PopulationRegion(string name, IEnumerable<PopulationServer> servers)
        {
            Name = name;
            Servers = servers.OrderByDescending(server => server.Players)
                .ThenBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
            Players = Servers.Sum(server => (long)server.Players);
        }
    }

    public sealed class PopulationServer
    {
        public string Name { get; }
        public string Region { get; }
        public string RegionGroup { get; }
        public int Players { get; }
        public int? Capacity { get; }
        public string PlayerCountText => Capacity.HasValue ? $"{Players:N0} / {Capacity:N0}" : $"{Players:N0} online";

        public PopulationServer(string name, string region, int players, int? capacity)
        {
            var cleanName = Regex.Replace(name ?? "", @"\^[0-9a-fA-F]{8}", "").Trim();
            Name = cleanName.Length == 0 ? "Unnamed server" : cleanName;
            Region = string.IsNullOrWhiteSpace(region) ? "Unknown" : region.Trim();
            RegionGroup = GroupRegion(Region);
            Players = players;
            Capacity = capacity;
        }

        static string GroupRegion(string region)
        {
            foreach (var group in new[] { "US", "EU", "Asia" })
                if (region.Equals(group, StringComparison.OrdinalIgnoreCase) || region.StartsWith(group + " ", StringComparison.OrdinalIgnoreCase))
                    return group;
            return region.Equals("AUS", StringComparison.OrdinalIgnoreCase) ? "Australia" : region;
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

                var listed = new List<PopulationServer>();
                foreach (var server in servers)
                {
                    if (server == null || !server.PlayerCount.HasValue || server.PlayerCount < 0 || server.MaxPlayers < 0)
                        throw new InvalidDataException("Northstar returned an invalid player count.");
                    listed.Add(new PopulationServer(server.Name, server.Region, server.PlayerCount.Value, server.MaxPlayers));
                }
                return new ServerPopulation(listed, DateTimeOffset.UtcNow);
            }
        }

        [DataContract]
        sealed class Server
        {
            [DataMember(Name = "playerCount", IsRequired = true)]
            public int? PlayerCount { get; set; }
            [DataMember(Name = "name")]
            public string Name { get; set; }
            [DataMember(Name = "region")]
            public string Region { get; set; }
            [DataMember(Name = "maxPlayers")]
            public int? MaxPlayers { get; set; }
        }
    }
}
