using System.Text.Json;
using System.Threading.Channels;
using Confluent.Kafka;
using Ietp.Telemetry.Contracts;
using Ietp.Telemetry.Domain;
using Ietp.BuildingBlocks;
using Observability = Ietp.BuildingBlocks.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Ietp.OpcUa.Client;

public sealed partial class OpcUaClientWorker : BackgroundService
{
    private readonly ClientOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OpcUaClientWorker> _logger;
    private readonly Channel<TelemetryEvent> _buffer;
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private volatile bool _connected;
    private int _reconnectionAttempts;
    private long _publishedEvents;
    private long _errors;
    private string? _lastError;

    public OpcUaClientWorker(IOptions<ClientOptions> options, IHostEnvironment environment, ILogger<OpcUaClientWorker> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
        _buffer = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(_options.BufferCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = await CreateConfigurationAsync(stoppingToken).ConfigureAwait(false);
        var publisher = PublishAsync(stoppingToken);
        var attempt = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndMonitorAsync(configuration, () =>
                    {
                        attempt = 0;
                        _reconnectionAttempts = 0;
                    }, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    attempt++;
                    _reconnectionAttempts = attempt;
                    _lastError = exception.Message;
                    _connected = false;
                    Observability.Retries.Add(1, new("service", "opcua-client"), new("server.id", _options.ServerId));
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)))) +
                        TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                    ConnectionFailed(_logger, _options.ServerId, attempt, delay.TotalSeconds, exception);
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _buffer.Writer.TryComplete();
            await publisher.ConfigureAwait(false);
        }
    }

    private async Task ConnectAndMonitorAsync(
        ApplicationConfiguration configuration,
        Action onConnected,
        CancellationToken cancellationToken)
    {
        var endpointDescription = await CoreClientUtils.SelectEndpointAsync(
            configuration, _options.Endpoint, _options.UseSecurity, 15_000, _telemetry, cancellationToken).ConfigureAwait(false);
        var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(configuration));
        var session = await new DefaultSessionFactory(_telemetry).CreateAsync(
            configuration,
            endpoint,
            updateBeforeConnect: false,
            checkDomain: true,
            sessionName: _options.ClientId,
            sessionTimeout: 60_000,
            identity: new UserIdentity(new AnonymousIdentityToken()),
            preferredLocales: null,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var namespaceIndex = session.NamespaceUris.GetIndex(_options.NamespaceUri);
            if (namespaceIndex < 0)
            {
                throw new InvalidOperationException($"Server does not expose configured namespace {_options.NamespaceUri}.");
            }

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.KeepAlive += (_, args) =>
            {
                if (ServiceResult.IsBad(args.Status)) disconnected.TrySetResult();
            };

            var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = _options.PublishingIntervalMilliseconds,
                KeepAliveCount = 10,
                LifetimeCount = 30,
                PublishingEnabled = true
            };
            foreach (var measurement in _options.Measurements)
            {
                var item = new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = measurement,
                    StartNodeId = new NodeId($"asset/{_options.AssetId}/{measurement}", (ushort)namespaceIndex),
                    AttributeId = Attributes.Value,
                    SamplingInterval = _options.PublishingIntervalMilliseconds,
                    QueueSize = 10,
                    DiscardOldest = true
                };
                item.Notification += OnNotification;
                subscription.AddItem(item);
            }

            session.AddSubscription(subscription);
            await subscription.CreateAsync(cancellationToken).ConfigureAwait(false);
            _connected = true;
            _lastError = null;
            onConnected();
            Connected(_logger, _options.ServerId, checked((int)subscription.MonitoredItemCount));
            await disconnected.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException($"OPC UA keep-alive failed for server {_options.ServerId}.");
        }
        finally
        {
            _connected = false;
            try
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                session.Dispose();
            }
        }
    }

    private void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs _)
    {
        foreach (var value in item.DequeueValues())
        {
            Observability.OpcUaNotifications.Add(1, new KeyValuePair<string, object?>("client.id", _options.ClientId));
            var telemetryEvent = Map(item.DisplayName, value);
            if (!_buffer.Writer.TryWrite(telemetryEvent))
            {
                Interlocked.Increment(ref _errors);
                _lastError = "The bounded telemetry buffer is full; an OPC UA notification was not queued.";
                Observability.OpcUaBufferDrops.Add(1, new KeyValuePair<string, object?>("client.id", _options.ClientId));
                BufferFull(_logger, _options.ClientId, _options.BufferCapacity);
            }
        }
    }

    private TelemetryEvent Map(string measurement, DataValue dataValue)
    {
        var key = SeriesIdentity.CreateKey("dk", "demo", _options.SiteId, _options.AssetId, measurement);
        var value = dataValue.Value switch
        {
            bool boolean => (TelemetryValue)new BooleanTelemetryValue(boolean),
            string text => new TextTelemetryValue(text),
            IConvertible number => new NumericTelemetryValue(number.ToDouble(System.Globalization.CultureInfo.InvariantCulture)),
            null => new TextTelemetryValue(string.Empty),
            _ => new TextTelemetryValue(dataValue.Value.ToString() ?? string.Empty)
        };
        var received = DateTimeOffset.UtcNow;

        return new TelemetryEvent(
            Guid.NewGuid(),
            TelemetryEvent.CurrentSchemaVersion,
            SeriesIdentity.CreateId(key),
            key,
            _options.ServerId,
            _options.ClientId,
            value,
            value.GetType().Name,
            MeasurementUnits.Get(measurement),
            dataValue.SourceTimestamp == DateTime.MinValue ? received : new DateTimeOffset(dataValue.SourceTimestamp, TimeSpan.Zero),
            dataValue.ServerTimestamp == DateTime.MinValue ? received : new DateTimeOffset(dataValue.ServerTimestamp, TimeSpan.Zero),
            received,
            StatusCode.IsGood(dataValue.StatusCode) ? "Good" : StatusCode.IsUncertain(dataValue.StatusCode) ? "Uncertain" : "Bad",
            dataValue.StatusCode.Code,
            Guid.NewGuid().ToString("N"));
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _options.KafkaBootstrapServers,
            ClientId = _options.ClientId,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageSendMaxRetries = 8,
            RetryBackoffMs = 250,
            MessageTimeoutMs = 30_000,
            LingerMs = 10
        };
        using var producer = new ProducerBuilder<string, string>(config).Build();
        var heartbeatTask = PublishHeartbeatsAsync(producer, cancellationToken);
        try
        {
            await foreach (var telemetryEvent in _buffer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                using var activity = Observability.Activities.StartActivity("kafka.produce telemetry.raw", System.Diagnostics.ActivityKind.Producer);
                activity?.SetTag("messaging.destination.name", _options.KafkaTopic);
                activity?.SetTag("telemetry.series_id", telemetryEvent.SeriesId);
                var headers = new Headers { { "correlation-id", System.Text.Encoding.UTF8.GetBytes(telemetryEvent.CorrelationId) } };
                if (activity?.Id is { } traceParent) headers.Add("traceparent", System.Text.Encoding.UTF8.GetBytes(traceParent));
                await producer.ProduceAsync(_options.KafkaTopic, new Message<string, string>
                {
                    Key = telemetryEvent.SeriesId.ToString("D"), Value = JsonSerializer.Serialize(telemetryEvent), Headers = headers
                }, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _publishedEvents);
                Observability.KafkaPublished.Add(1, new KeyValuePair<string, object?>("topic", _options.KafkaTopic));
            }
        }
        finally
        {
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        producer.Flush(cancellationToken);
    }

    private async Task PublishHeartbeatsAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            var health = !_connected ? "Disconnected" : _lastError is null ? "Healthy" : "Degraded";
            var heartbeat = new ServiceHeartbeat("opcua-client", _options.ClientId, health,
                _startedAt, DateTimeOffset.UtcNow, Interlocked.Read(ref _publishedEvents), Interlocked.Read(ref _errors), 0, _options.ServerId, _options.ClientId,
                _connected ? 1 : 0, _connected ? _options.Measurements.Length : 0, _reconnectionAttempts, _lastError);
            await producer.ProduceAsync(_options.StatusTopic, new Message<string, string>
            {
                Key = heartbeat.InstanceId, Value = JsonSerializer.Serialize(heartbeat)
            }, cancellationToken).ConfigureAwait(false);
        } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken cancellationToken)
    {
        var pkiRoot = Path.GetFullPath(_options.PkiPath, _environment.ContentRootPath);
        var configuration = new ApplicationConfiguration(_telemetry)
        {
            ApplicationName = $"IETP {_options.ClientId}",
            ApplicationUri = $"urn:ietp:client:{_options.ClientId}",
            ProductUri = "urn:ietp:industrial-energy-telemetry",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiRoot, "own"),
                    SubjectName = $"CN=IETP {_options.ClientId},O=IETP"
                },
                TrustedPeerCertificates = TrustList(Path.Combine(pkiRoot, "trusted")),
                TrustedIssuerCertificates = TrustList(Path.Combine(pkiRoot, "issuers")),
                RejectedCertificateStore = TrustList(Path.Combine(pkiRoot, "rejected")),
                AutoAcceptUntrustedCertificates = false,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048
            },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60_000 },
            TransportQuotas = new TransportQuotas { OperationTimeout = 15_000 }
        };
        await configuration.ValidateAsync(ApplicationType.Client, cancellationToken).ConfigureAwait(false);
        var application = new ApplicationInstance(_telemetry)
        {
            ApplicationName = configuration.ApplicationName,
            ApplicationType = ApplicationType.Client,
            ApplicationConfiguration = configuration
        };
        if (!await application.CheckApplicationInstanceCertificatesAsync(false, 2048, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A valid OPC UA client certificate could not be created or loaded.");
        }
        return configuration;
    }

    private static CertificateTrustList TrustList(string path) => new()
    {
        StoreType = CertificateStoreType.Directory,
        StorePath = path
    };

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Connected to OPC UA server {ServerId} with {MonitoredItemCount} monitored items")]
    private static partial void Connected(ILogger logger, string serverId, int monitoredItemCount);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Connection to {ServerId} failed on attempt {Attempt}; retrying in {DelaySeconds:F1} seconds")]
    private static partial void ConnectionFailed(ILogger logger, string serverId, int attempt, double delaySeconds, Exception exception);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error, Message = "Client {ClientId} bounded buffer of {Capacity} events is full; OPC UA notification could not be accepted")]
    private static partial void BufferFull(ILogger logger, string clientId, int capacity);
}
