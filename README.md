# Auto Vehicle Emulator

ASP.NET Core web app for sending a VDA 5050 2.0.0 vehicle state to FMS over
MQTT. The built-in web page lets you set GPS coordinates or start a small
simulated movement loop.

Open `AutoVehicleEmulator.sln` in Visual Studio 2022 17.12 or later (with the
.NET 9 SDK installed).

## Docker

Build the image from this repository root:

```powershell
docker build -t auto-vehicle-emulator .
```

Run it on port `5120`, configuring the MQTT broker address reachable from the
container:

```powershell
docker run --rm -p 5120:8080 `
  -e VehicleEmulator__BrokerHost=host.docker.internal `
  -e VehicleEmulator__BrokerPort=1883 `
  auto-vehicle-emulator
```

For Docker Compose, use the broker's service name (for example `rabbitmq`)
instead of `host.docker.internal`.

## Run

The MQTT broker must be reachable from both apps and expose its MQTT listener
(RabbitMQ's MQTT plugin commonly listens on port `1883`). The emulator does not
send coordinates to the FMS HTTP API; it publishes VDA 5050 MQTT messages,
which is the vehicle transport consumed by FMS.

1. Enable the RabbitMQ MQTT plugin and expose port `1883`.
2. Start FMS with its development MQTT receiver enabled:

   ```powershell
   $env:Vda5050__Enabled = "true"
   $env:Vda5050__Broker__Host = "localhost"
   $env:Vda5050__Broker__Port = "1883"
   dotnet run --project C:\work\01_source\FMS\FMS\FMS.csproj
   ```

   If FMS runs in Docker, use the broker's Docker-network hostname instead of
   `localhost`.
3. Start the emulator:

   ```powershell
   $env:VehicleEmulator__BrokerHost = "localhost"
   $env:VehicleEmulator__BrokerPort = "1883"
   dotnet run --project C:\work\01_source\AutoVehicleEmulator\app\app.csproj
   ```
4. Open the emulator URL printed by `dotnet run` (normally
   `http://localhost:5120`). Enter coordinates or start simulated movement.
   The FMS dashboard is at `http://localhost:5119` with its default launch
   profile and shows the vehicle received through `GET /api/vehicles`.

The emulator API is:

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/health` | Check app availability |
| `GET` | `/api/vehicle` | Read current coordinates and MQTT status |
| `PUT` | `/api/vehicle/position` | Set GPS coordinates |
| `POST` | `/api/vehicle/simulation` | Start/stop simulated movement |

Example:

```powershell
Invoke-RestMethod -Method Put -Uri http://localhost:5120/api/vehicle/position `
  -ContentType "application/json" -Body '{"latitude":35.681236,"longitude":139.767125}'
```

Configure identity and broker credentials through the `VehicleEmulator`
settings section or environment variables such as
`VehicleEmulator__Manufacturer`, `VehicleEmulator__SerialNumber`,
`VehicleEmulator__Username`, and `VehicleEmulator__Password`. FMS subscribes
to `uagv/v2/{manufacturer}/{serialNumber}/state` and `/connection`; the state
includes the GPS coordinate extension consumed by the FMS map.
