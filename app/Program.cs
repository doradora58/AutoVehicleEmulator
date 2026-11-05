using AutoVehicleEmulator;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<VehicleEmulatorSettings>()
    .Bind(builder.Configuration.GetSection("VehicleEmulator"))
    .Validate(settings => IsValidTopicSegment(settings.Manufacturer), "Manufacturer must be a non-empty MQTT topic segment.")
    .Validate(settings => IsValidTopicSegment(settings.SerialNumber), "Serial number must be a non-empty MQTT topic segment.")
    .Validate(settings => IsValidTopicRoot(settings.TopicRoot), "Topic root must not contain empty segments or MQTT wildcards.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.BrokerHost), "Broker host is required.")
    .Validate(settings => settings.BrokerPort is >= 1 and <= 65535, "Broker port must be between 1 and 65535.")
    .Validate(settings => settings.PublishIntervalMilliseconds >= 250, "Publish interval must be at least 250 ms.")
    .Validate(settings => string.IsNullOrWhiteSpace(settings.Password) || !string.IsNullOrWhiteSpace(settings.Username),
        "Username is required when a password is configured.")
    .ValidateOnStart();
builder.Services.AddSingleton<VehicleEmulatorService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<VehicleEmulatorService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/vehicle", (VehicleEmulatorService emulator) =>
    Results.Ok(emulator.GetSnapshot()));
app.MapPut("/api/vehicle/position", (SetPositionRequest request, VehicleEmulatorService emulator) =>
{
    if (!double.IsFinite(request.Latitude) || request.Latitude is < -90 or > 90)
    {
        return Results.BadRequest(new { error = "Latitude must be between -90 and 90." });
    }

    if (!double.IsFinite(request.Longitude) || request.Longitude is < -180 or > 180)
    {
        return Results.BadRequest(new { error = "Longitude must be between -180 and 180." });
    }

    return Results.Accepted("/api/vehicle", emulator.SetPosition(request.Latitude, request.Longitude));
});
app.MapPost("/api/vehicle/simulation", (SetSimulationRequest request, VehicleEmulatorService emulator) =>
    Results.Ok(emulator.SetSimulation(request.Enabled)));

app.Run();

static bool IsValidTopicSegment(string value) =>
    !string.IsNullOrWhiteSpace(value) && !value.Contains('/') && !value.Contains('+') && !value.Contains('#');

static bool IsValidTopicRoot(string value) =>
    !string.IsNullOrWhiteSpace(value) &&
    value.Trim('/') == value &&
    value.Split('/').All(segment => IsValidTopicSegment(segment));
