using Ietp.OpcUa.Client;
using Ietp.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<ClientOptions>()
    .Bind(builder.Configuration.GetSection("OpcUaClient"))
    .Validate(options => options.Measurements.Length > 0, "At least one measurement must be configured.")
    .Validate(options => options.BufferCapacity > 0, "Buffer capacity must be positive.")
    .ValidateOnStart();
builder.Services.AddHostedService<OpcUaClientWorker>();
builder.Services.AddIetpObservability("ietp-opcua-client");
await builder.Build().RunAsync().ConfigureAwait(false);
