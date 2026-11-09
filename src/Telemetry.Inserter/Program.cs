using Ietp.Telemetry.Inserter;
using Ietp.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<InserterOptions>().Bind(builder.Configuration.GetSection("Inserter"))
    .Validate(options => options.BatchSize is > 0 and <= 2000, "Batch size must be between 1 and 2000.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Inserter:ConnectionString is required.")
    .ValidateOnStart();
builder.Services.AddHostedService<InserterWorker>();
builder.Services.AddIetpObservability("ietp-telemetry-inserter");
await builder.Build().RunAsync().ConfigureAwait(false);
