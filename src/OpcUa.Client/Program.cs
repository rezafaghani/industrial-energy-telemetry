using Ietp.OpcUa.Client;
using Ietp.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<ClientOptions>()
    .Bind(builder.Configuration.GetSection("OpcUaClient"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CoordinatorUrl) || options.Measurements.Length > 0,
        "At least one static measurement or a coordinator URL must be configured.")
    .Validate(options => options.BufferCapacity > 0, "Buffer capacity must be positive.")
    .Validate(options => options.CollectorCapacity == 1, "This collector version supports one assigned endpoint.")
    .Validate(options => options.LeaseSeconds >= 15 && options.CoordinatorIntervalSeconds > 0 && options.CoordinatorIntervalSeconds < options.LeaseSeconds,
        "The coordinator interval must be positive and shorter than a lease of at least 15 seconds.")
    .ValidateOnStart();
builder.Services.AddHostedService<OpcUaClientWorker>();
builder.Services.AddHostedService<CoordinatedClientWorker>();
builder.Services.AddIetpObservability("ietp-opcua-client");
await builder.Build().RunAsync().ConfigureAwait(false);
