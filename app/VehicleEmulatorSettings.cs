namespace AutoVehicleEmulator;

public sealed class VehicleEmulatorSettings
{
    public string Manufacturer { get; set; } = "Example";
    public string SerialNumber { get; set; } = "AGV-001";
    public string TopicRoot { get; set; } = "uagv/v2";
    public string BrokerHost { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseTls { get; set; }
    public int PublishIntervalMilliseconds { get; set; } = 2000;
}
