using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigWalkVRInstaller.Services
{
    public sealed class PopulationRegion
    {
        public string Name { get; }
        public long Players { get; }
        public string Summary => $"{Players:N0} {(Players == 1 ? "player" : "players")}";

        public PopulationRegion(string name, long players)
        {
            Name = name;
            Players = players;
        }
    }

    public static class VrPopulationClient
    {
        static readonly HttpClient Http;

        static VrPopulationClient()
        {
            Http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = 4 * 1024 * 1024
            };
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("CircuitLordVRModInstaller");
        }

        // The same regional directories used by Titanfall2VR.Multiplayer's main menu.
        public static Task<PopulationRegion[]> Fetch(CancellationToken cancellationToken) => Task.WhenAll(
            FetchRegion("NA", "http://mp.jumpkitvr.com:37015/listing", cancellationToken),
            FetchRegion("EU", "http://mp-eu.jumpkitvr.com:37015/listing", cancellationToken));

        static async Task<PopulationRegion> FetchRegion(string region, string url, CancellationToken cancellationToken)
        {
            using (var content = new StringContent("{}", Encoding.UTF8, "application/json"))
            using (var response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var listing = JsonUtil.Deserialize<Listing>(json);
                if (listing == null || !listing.Online.HasValue || listing.Online < 0)
                    throw new InvalidDataException("The VR directory returned an invalid player count.");
                return new PopulationRegion(region, listing.Online.Value);
            }
        }

        [DataContract]
        sealed class Listing
        {
            [DataMember(Name = "online", IsRequired = true)]
            public int? Online { get; set; }
        }
    }
}
