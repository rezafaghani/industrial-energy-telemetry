using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ietp.BuildingBlocks;

public static class Telemetry
{
    public const string SourceName = "Ietp.Telemetry";
    public static readonly ActivitySource Activities = new(SourceName);
    public static readonly Meter Meter = new(SourceName);
    public static readonly Counter<long> OpcUaNotifications = Meter.CreateCounter<long>("ietp.opcua.notifications");
    public static readonly Counter<long> OpcUaBufferDrops = Meter.CreateCounter<long>("ietp.opcua.buffer.drops");
    public static readonly Counter<long> KafkaPublished = Meter.CreateCounter<long>("ietp.kafka.published");
    public static readonly Counter<long> Processed = Meter.CreateCounter<long>("ietp.processor.events");
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("ietp.processor.dead_lettered");
    public static readonly Counter<long> Inserted = Meter.CreateCounter<long>("ietp.inserter.events");
    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("ietp.retries");
    public static readonly Histogram<double> ProcessingLatency = Meter.CreateHistogram<double>("ietp.processor.latency", "ms");
    public static readonly Histogram<double> InsertionLatency = Meter.CreateHistogram<double>("ietp.inserter.latency", "ms");

    public static IServiceCollection AddIetpObservability(this IServiceCollection services, string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing.AddSource(SourceName).AddOtlpExporter())
            .WithMetrics(metrics => metrics.AddMeter(SourceName).AddOtlpExporter());
        return services;
    }
}
