using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ietp.OpcUa.Client;

public sealed partial class CoordinatedClientWorker(
    IOptions<ClientOptions> configuredOptions,
    IHostEnvironment environment,
    ILoggerFactory loggerFactory,
    ILogger<CoordinatedClientWorker> logger) : BackgroundService
{
    private readonly ClientOptions _options = configuredOptions.Value;
    private readonly string _collectorId = configuredOptions.Value.CollectorId
        ?? Environment.GetEnvironmentVariable("HOSTNAME")
        ?? Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CoordinatorUrl)) return;
        using var http = new HttpClient { BaseAddress = new Uri(_options.CoordinatorUrl) };
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CoordinatorIntervalSeconds));
        OpcUaClientWorker? session = null;
        CancellationTokenSource? sessionCancellation = null;
        Task? sessionTask = null;
        Assignment? assignment = null;

        try
        {
            await RegisterAsync(http, stoppingToken).ConfigureAwait(false);
            do
            {
                try
                {
                    if (assignment is null)
                    {
                        await ReportHealthAsync(http, session, stoppingToken).ConfigureAwait(false);
                        using var acquisition = await http.PostAsync($"api/control-plane/collectors/{Uri.EscapeDataString(_collectorId)}/assignments?leaseSeconds={_options.LeaseSeconds}", null, stoppingToken)
                            .ConfigureAwait(false);
                        acquisition.EnsureSuccessStatusCode();
                        assignment = (await acquisition.Content.ReadFromJsonAsync<List<Assignment>>(cancellationToken: stoppingToken)
                            .ConfigureAwait(false))?.FirstOrDefault();
                        if (assignment is not null)
                        {
                            var configuration = await http.GetFromJsonAsync<EndpointConfiguration>(
                                $"api/control-plane/collectors/{Uri.EscapeDataString(_collectorId)}/assignments/{assignment.EndpointId}/configuration?leaseEpoch={assignment.LeaseEpoch}", stoppingToken).ConfigureAwait(false)
                                ?? throw new InvalidDataException("Coordinator returned an empty endpoint configuration.");
                            sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                            session = new OpcUaClientWorker(Options.Create(ToClientOptions(configuration)), environment,
                                loggerFactory.CreateLogger<OpcUaClientWorker>());
                            sessionTask = session.RunAsync(sessionCancellation.Token);
                        }
                    }
                    else
                    {
                        await ReportHealthAsync(http, session, stoppingToken).ConfigureAwait(false);
                        using var renewal = await http.PutAsJsonAsync(
                            $"api/control-plane/collectors/{Uri.EscapeDataString(_collectorId)}/assignments/{assignment.EndpointId}",
                            new { assignment.LeaseEpoch, LeaseSeconds = _options.LeaseSeconds }, stoppingToken).ConfigureAwait(false);
                        if (!renewal.IsSuccessStatusCode || sessionTask?.IsCompleted == true)
                        {
                            sessionCancellation?.Cancel();
                            if (sessionTask is not null) await IgnoreCancellationAsync(sessionTask).ConfigureAwait(false);
                            sessionCancellation?.Dispose();
                            sessionCancellation = null;
                            session = null;
                            sessionTask = null;
                            assignment = null;
                        }
                    }
                }
                catch (HttpRequestException exception)
                {
                    CoordinatorUnavailable(logger, _collectorId, exception);
                    sessionCancellation?.Cancel();
                    if (sessionTask is not null) await IgnoreCancellationAsync(sessionTask).ConfigureAwait(false);
                    sessionCancellation?.Dispose();
                    sessionCancellation = null;
                    session = null;
                    sessionTask = null;
                    assignment = null;
                    await RegisterAsync(http, stoppingToken).ConfigureAwait(false);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        finally
        {
            sessionCancellation?.Cancel();
            if (sessionTask is not null) await IgnoreCancellationAsync(sessionTask).ConfigureAwait(false);
            try { await http.DeleteAsync($"api/control-plane/collectors/{Uri.EscapeDataString(_collectorId)}", CancellationToken.None).ConfigureAwait(false); }
            catch (HttpRequestException) { }
            sessionCancellation?.Dispose();
        }
    }

    private async Task RegisterAsync(HttpClient http, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await http.PostAsJsonAsync("api/control-plane/collectors", new
                {
                    CollectorId = _collectorId,
                    SiteKey = string.IsNullOrWhiteSpace(_options.SiteId) ? null : _options.SiteId,
                    Capacity = _options.CollectorCapacity,
                    Metadata = new Dictionary<string, string> { ["version"] = typeof(CoordinatedClientWorker).Assembly.GetName().Version?.ToString() ?? "unknown" }
                }, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException exception)
            {
                RegistrationFailed(logger, _collectorId, exception);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReportHealthAsync(HttpClient http, OpcUaClientWorker? session, CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync($"api/control-plane/collectors/{Uri.EscapeDataString(_collectorId)}/health", new
        {
            Health = session is null ? "Available" : session.IsConnected ? "Healthy" : "Connecting",
            ActiveSessions = session?.IsConnected == true ? 1 : 0,
            BufferItems = 0,
            BufferBytes = 0,
            LastError = session?.LastError
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private ClientOptions ToClientOptions(EndpointConfiguration configuration) => new()
    {
        ClientId = $"{configuration.EndpointKey}-client",
        ServerId = configuration.EndpointKey,
        Endpoint = configuration.Endpoint,
        NamespaceUri = configuration.NamespaceUri,
        SiteId = configuration.SiteKey,
        AssetId = configuration.AssetKey,
        Measurements = configuration.Tags.Select(tag => tag.Measurement).ToArray(),
        KafkaBootstrapServers = _options.KafkaBootstrapServers,
        KafkaTopic = _options.KafkaTopic,
        StatusTopic = _options.StatusTopic,
        BufferCapacity = _options.BufferCapacity,
        PublishingIntervalMilliseconds = _options.PublishingIntervalMilliseconds,
        UseSecurity = !string.Equals(configuration.SecurityMode, "None", StringComparison.OrdinalIgnoreCase),
        PkiPath = _options.PkiPath
    };

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private sealed record Assignment(Guid EndpointId, long LeaseEpoch);
    private sealed record EndpointTag(string Measurement);
    private sealed record EndpointConfiguration(Guid EndpointId, string EndpointKey, string Endpoint, string NamespaceUri,
        string SiteKey, string AssetKey, string SecurityPolicy, string SecurityMode, long ConfigurationVersion,
        IReadOnlyList<EndpointTag> Tags);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Collector {CollectorId} could not reach its coordinator")]
    private static partial void CoordinatorUnavailable(ILogger logger, string collectorId, Exception exception);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "Collector {CollectorId} registration failed; retrying")]
    private static partial void RegistrationFailed(ILogger logger, string collectorId, Exception exception);
}
