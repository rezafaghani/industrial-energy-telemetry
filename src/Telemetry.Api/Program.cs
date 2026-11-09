using Ietp.Telemetry.Api;
using Ietp.BuildingBlocks;
using Npgsql;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Telemetry")
    ?? throw new InvalidOperationException("ConnectionStrings:Telemetry is required.")));
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddHostedService<LiveTelemetryBroadcaster>();
builder.Services.AddHostedService<ApiHeartbeatWorker>();
builder.Services.AddIetpObservability("ietp-telemetry-api");
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddAspNetCoreInstrumentation());

var app = builder.Build();
app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await Results.Problem("The request could not be completed.", statusCode: 500).ExecuteAsync(context);
}));
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapHub<TelemetryHub>("/hubs/telemetry");
app.MapOpenApi();
ApiEndpoints.Map(app);
await app.RunAsync();
