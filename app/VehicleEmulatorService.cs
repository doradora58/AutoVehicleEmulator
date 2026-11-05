using System.Text.Json;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace AutoVehicleEmulator;

public sealed class VehicleEmulatorService : BackgroundService
{
    private readonly object stateLock = new();
    private readonly VehicleEmulatorSettings settings;
    private readonly ILogger<VehicleEmulatorService> logger;
    private readonly IMqttClient mqttClient;
    private VehiclePosition position = new(35.170915, 136.881537);
    private VehiclePosition simulationOrigin = new(35.170915, 136.881537);
    private bool simulationEnabled;
    private double simulationAngle;
    private int headerId;
    private DateTimeOffset? lastPublishedAt;
    private string? lastError;

    public VehicleEmulatorService(
        IOptions<VehicleEmulatorSettings> settings,
        ILogger<VehicleEmulatorService> logger)
    {
        this.settings = settings.Value;
        this.settings.TopicRoot = this.settings.TopicRoot.Trim('/');
        this.logger = logger;
        mqttClient = new MqttFactory().CreateMqttClient();
    }

    public VehicleEmulatorSnapshot GetSnapshot()
    {
        lock (stateLock)
        {
            return new VehicleEmulatorSnapshot(
                settings.Manufacturer,
                settings.SerialNumber,
                position,
                simulationEnabled,
                mqttClient.IsConnected,
                lastPublishedAt,
                lastError);
        }
    }

    public VehicleEmulatorSnapshot SetPosition(double latitude, double longitude)
    {
        lock (stateLock)
        {
            position = new VehiclePosition(latitude, longitude);
            simulationOrigin = position;
            simulationAngle = 0;
            return GetSnapshot();
        }
    }

    public VehicleEmulatorSnapshot SetSimulation(bool enabled)
    {
        lock (stateLock)
        {
            simulationEnabled = enabled;
            simulationOrigin = position;
            simulationAngle = 0;
            return GetSnapshot();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stateTopic = $"{settings.TopicRoot}/{settings.Manufacturer}/{settings.SerialNumber}/state";
        var connectionTopic = $"{settings.TopicRoot}/{settings.Manufacturer}/{settings.SerialNumber}/connection";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!mqttClient.IsConnected)
                {
                    await mqttClient.ConnectAsync(CreateClientOptions(), stoppingToken);
                    await PublishAsync(connectionTopic, "ONLINE", retain: true, stoppingToken);
                    logger.LogInformation(
                        "Connected to MQTT broker {Host}:{Port}; publishing VDA 5050 state to {Topic}.",
                        settings.BrokerHost,
                        settings.BrokerPort,
                        stateTopic);
                }

                AdvanceSimulation();
                await PublishAsync(stateTopic, CreateStatePayload(), retain: false, stoppingToken);
                lock (stateLock)
                {
                    lastPublishedAt = DateTimeOffset.UtcNow;
                    lastError = null;
                }

                await Task.Delay(settings.PublishIntervalMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                lock (stateLock)
                {
                    lastError = exception.Message;
                }

                logger.LogWarning(
                    exception,
                    "Could not publish vehicle state to MQTT broker {Host}:{Port}; retrying.",
                    settings.BrokerHost,
                    settings.BrokerPort);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        if (mqttClient.IsConnected)
        {
            try
            {
                await PublishAsync(connectionTopic, "OFFLINE", retain: true, CancellationToken.None);
                await mqttClient.DisconnectAsync(cancellationToken: CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not publish vehicle offline status during shutdown.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        mqttClient.Dispose();
    }

    private MqttClientOptions CreateClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId($"vehicle-emulator-{settings.Manufacturer}-{settings.SerialNumber}")
            .WithTcpServer(settings.BrokerHost, settings.BrokerPort)
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            builder.WithCredentials(settings.Username, settings.Password ?? string.Empty);
        }

        if (settings.UseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        return builder.Build();
    }

    private async Task PublishAsync(
        string topic,
        string payload,
        bool retain,
        CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build();
        await mqttClient.PublishAsync(message, cancellationToken);
    }

    private string CreateStatePayload()
    {
        VehiclePosition currentPosition;
        lock (stateLock)
        {
            currentPosition = position;
            headerId++;
        }

        var state = new
        {
            headerId,
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            version = "2.0.0",
            manufacturer = settings.Manufacturer,
            serialNumber = settings.SerialNumber,
            orderId = string.Empty,
            orderUpdateId = 0,
            lastNodeId = string.Empty,
            lastNodeSequenceId = 0,
            nodeStates = Array.Empty<object>(),
            edgeStates = Array.Empty<object>(),
            agvPosition = new
            {
                x = 0.0,
                y = 0.0,
                theta = 0.0,
                positionInitialized = true,
                localizationScore = 1.0,
                mapId = "emulator",
                latitude = currentPosition.Latitude,
                longitude = currentPosition.Longitude
            },
            velocity = new { vx = 0.0, vy = 0.0, omega = 0.0 },
            loads = Array.Empty<object>(),
            driving = false,
            paused = false,
            newBaseRequest = false,
            distanceSinceLastNode = 0.0,
            operatingMode = "AUTOMATIC",
            errors = Array.Empty<object>(),
            information = Array.Empty<object>(),
            safetyState = new { eStop = "NONE", fieldViolation = false }
        };

        return JsonSerializer.Serialize(state);
    }

    private void AdvanceSimulation()
    {
        lock (stateLock)
        {
            if (!simulationEnabled)
            {
                return;
            }

            simulationAngle += 0.08;
            position = new VehiclePosition(
                simulationOrigin.Latitude + Math.Sin(simulationAngle) * 0.00015,
                simulationOrigin.Longitude + Math.Cos(simulationAngle) * 0.0002);
        }
    }
}
