using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ietp.Telemetry.EndToEndTests;

public sealed class CompletePathTests
{
    private static readonly string[] ExpectedSites = ["windfarm01", "solarfarm01", "grid01", "battery01", "plant01"];

    [Fact, Trait("Category", "EndToEnd")]
    public async Task AllFiveSourcesReachDatabaseApiAndFrontend()
    {
        var baseUrl = Environment.GetEnvironmentVariable("IETP_BASE_URL")
            ?? throw new InvalidOperationException("IETP_BASE_URL is required for end-to-end tests.");
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        JsonElement[] latest = [];
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                latest = await client.GetFromJsonAsync<JsonElement[]>("/api/telemetry/latest", timeout.Token) ?? [];
                var keys = latest.Select(row => row.GetProperty("series_key").GetString() ?? string.Empty).ToArray();
                if (ExpectedSites.All(site => keys.Any(key => key.Contains($".{site}.", StringComparison.Ordinal)))) break;
            }
            catch (HttpRequestException) { }
            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
        }

        Assert.True(latest.Length >= 31, $"Expected at least 31 latest values, received {latest.Length}.");
        var series = await client.GetFromJsonAsync<JsonElement[]>("/api/series?pageSize=200", timeout.Token) ?? [];
        Assert.Equal(31, series.Length);
        var servers = await client.GetFromJsonAsync<JsonElement[]>("/api/servers", timeout.Token) ?? [];
        var clients = await client.GetFromJsonAsync<JsonElement[]>("/api/clients", timeout.Token) ?? [];
        Assert.Equal(5, servers.Length);
        Assert.Equal(5, clients.Length);
        Assert.All(servers, server => Assert.Equal("Healthy", server.GetProperty("status").GetString()));
        Assert.All(clients, opcClient => Assert.Equal("Healthy", opcClient.GetProperty("status").GetString()));
        var html = await client.GetStringAsync("/", timeout.Token);
        Assert.Contains("<div id=\"root\"></div>", html, StringComparison.Ordinal);
    }
}
