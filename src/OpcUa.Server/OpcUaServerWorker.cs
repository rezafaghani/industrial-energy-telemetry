using Ietp.Telemetry.Domain.Simulation;
using Ietp.BuildingBlocks;
using Observability = Ietp.BuildingBlocks.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace Ietp.OpcUa.Server;

public sealed partial class OpcUaServerWorker(
    IOptions<ServerOptions> configuredOptions,
    IHostEnvironment environment,
    ILogger<OpcUaServerWorker> logger) : BackgroundService
{
    private readonly ServerOptions _options = configuredOptions.Value;
    private ApplicationInstance? _application;
    private IetpServer? _server;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var telemetry = DefaultTelemetry.Create(_ => { });
        var configuration = CreateConfiguration(telemetry);
        await configuration.ValidateAsync(ApplicationType.Server, stoppingToken).ConfigureAwait(false);

        _application = new ApplicationInstance(telemetry)
        {
            ApplicationName = configuration.ApplicationName,
            ApplicationType = ApplicationType.Server,
            ApplicationConfiguration = configuration
        };
        if (!await _application.CheckApplicationInstanceCertificatesAsync(false, 2048, stoppingToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A valid OPC UA application certificate could not be created or loaded.");
        }

        _server = new IetpServer(_options);
        await _application.StartAsync(_server).ConfigureAwait(false);
        ServerStarted(logger, _options.ServerId, _options.Endpoint);

        var simulation = new EnergySimulation(new SimulationOptions(
            _options.Profile,
            _options.Seed,
            TimeSpan.FromMilliseconds(_options.UpdateIntervalMilliseconds),
            _options.AnomalyProbability,
            _options.FailAfterTicks,
            _options.FailureDurationTicks));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.UpdateIntervalMilliseconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var snapshot = simulation.Next(DateTimeOffset.UtcNow);
            _server.EnergyNodeManager?.Apply(snapshot.Measurements, snapshot.SourceTimestamp, snapshot.Available);
            Observability.OpcUaNotifications.Add(snapshot.Measurements.Count, new KeyValuePair<string, object?>("server.id", _options.ServerId));
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            await _server.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private ApplicationConfiguration CreateConfiguration(ITelemetryContext telemetry)
    {
        var pkiRoot = Path.GetFullPath(_options.PkiPath, environment.ContentRootPath);
        var securityPolicies = new ServerSecurityPolicyCollection
        {
            new() { SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityPolicyUri = SecurityPolicies.Basic256Sha256 }
        };
        if (_options.AllowUnsecuredDevelopmentEndpoint && environment.IsDevelopment())
        {
            securityPolicies.Add(new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None });
            UnsecuredEndpointEnabled(logger, _options.ServerId);
        }

        return new ApplicationConfiguration(telemetry)
        {
            ApplicationName = $"IETP {_options.ServerId}",
            ApplicationUri = $"urn:ietp:server:{_options.ServerId}",
            ProductUri = "urn:ietp:industrial-energy-telemetry",
            ApplicationType = ApplicationType.Server,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiRoot, "own"),
                    SubjectName = $"CN=IETP {_options.ServerId},O=IETP"
                },
                TrustedPeerCertificates = TrustList(Path.Combine(pkiRoot, "trusted")),
                TrustedIssuerCertificates = TrustList(Path.Combine(pkiRoot, "issuers")),
                RejectedCertificateStore = TrustList(Path.Combine(pkiRoot, "rejected")),
                AutoAcceptUntrustedCertificates = false,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048
            },
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = [_options.Endpoint],
                SecurityPolicies = securityPolicies,
                UserTokenPolicies = [new UserTokenPolicy(UserTokenType.Anonymous)],
                DiagnosticsEnabled = true,
                MaxSessionCount = 100,
                MaxSubscriptionCount = 1000,
                MaxNotificationsPerPublish = 1000,
                MinRequestThreadCount = 2,
                MaxRequestThreadCount = 100,
                MaxQueuedRequestCount = 1000
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15_000,
                MaxStringLength = 1_048_576,
                MaxByteStringLength = 4_194_304,
                MaxArrayLength = 65_535,
                MaxMessageSize = 4_194_304,
                MaxBufferSize = 65_535,
                ChannelLifetime = 300_000,
                SecurityTokenLifetime = 3_600_000
            }
        };
    }

    private static CertificateTrustList TrustList(string path) => new()
    {
        StoreType = CertificateStoreType.Directory,
        StorePath = path
    };

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "OPC UA server {ServerId} listening at {Endpoint}")]
    private static partial void ServerStarted(ILogger logger, string serverId, string endpoint);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Development-only unsecured OPC UA endpoint is enabled for {ServerId}")]
    private static partial void UnsecuredEndpointEnabled(ILogger logger, string serverId);
}
