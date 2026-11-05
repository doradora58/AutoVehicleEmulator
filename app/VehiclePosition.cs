namespace AutoVehicleEmulator;

public sealed record VehiclePosition(double Latitude, double Longitude);

public sealed record VehicleEmulatorSnapshot(
    string Manufacturer,
    string SerialNumber,
    VehiclePosition Position,
    bool SimulationEnabled,
    bool MqttConnected,
    DateTimeOffset? LastPublishedAt,
    string? LastError);

public sealed record SetPositionRequest(double Latitude, double Longitude);

public sealed record SetSimulationRequest(bool Enabled);
