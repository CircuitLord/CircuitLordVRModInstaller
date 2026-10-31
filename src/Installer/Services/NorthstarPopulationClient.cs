using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BigWalkVRInstaller.Services
{
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
        static readonly HttpClient Http;

        static NorthstarPopulationClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = 4 * 1024 * 1024
            };
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("CircuitLordVRModInstaller");
        }

        public static async Task<PopulationRegion[]> Fetch(CancellationToken cancellationToken)
        {
            using (var response = await Http.GetAsync("https://northstar.tf/client/servers", cancellationToken).ConfigureAwait(false))
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
                return servers.GroupBy(server => GroupRegion(server.Region), StringComparer.OrdinalIgnoreCase)
                    .Select(group => new PopulationRegion(group.Key, group.Sum(server => (long)server.PlayerCount.Value), group.Count()))
                    .OrderBy(region => region.Name == "Unknown")
                    .ThenByDescending(region => region.Players)
                    .ThenBy(region => region.Name, StringComparer.OrdinalIgnoreCase).ToArray();
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
