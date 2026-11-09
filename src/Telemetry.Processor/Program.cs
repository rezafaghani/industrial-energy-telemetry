using Ietp.Telemetry.Processor;
using Ietp.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<ProcessorOptions>()
    .Bind(builder.Configuration.GetSection("Processor"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "Kafka bootstrap servers are required.")
    .ValidateOnStart();
builder.Services.AddHostedService<ProcessorWorker>();
builder.Services.AddIetpObservability("ietp-telemetry-processor");
await builder.Build().RunAsync().ConfigureAwait(false);
