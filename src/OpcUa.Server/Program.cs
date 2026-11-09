using Ietp.OpcUa.Server;
using Ietp.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection("OpcUa"))
    .Validate(options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _), "OpcUa:Endpoint must be an absolute URI.")
    .Validate(options => options.UpdateIntervalMilliseconds > 0, "Update interval must be positive.")
    .ValidateOnStart();
builder.Services.AddHostedService<OpcUaServerWorker>();
builder.Services.AddIetpObservability("ietp-opcua-server");
await builder.Build().RunAsync().ConfigureAwait(false);
