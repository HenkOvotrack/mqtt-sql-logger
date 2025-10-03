# MQTT SQL Logger

A production-ready .NET 8 application that subscribes to MQTT topics and logs messages to a SQL Server database. Designed for IoT data collection, home automation logging, and MQTT message persistence.

## 🚀 Features

- **MQTT Integration**: Subscribe to multiple topics with wildcards support
- **SQL Server Logging**: Automatic table creation and optimized message storage
- **Resilient**: Automatic reconnection with exponential backoff for both MQTT and SQL
- **Docker Ready**: Complete containerization with Portainer support
- **Configurable**: Environment variable configuration for all settings
- **Observability**: Structured logging with message counters and health monitoring
- **Production Ready**: Non-root Docker execution, health checks, and error handling

## 🏗️ Architecture

```
MQTT Broker → MQTT SQL Logger → SQL Server Database
     ↓              ↓                    ↓
  Topics        Processing           Structured
  Messages      & Parsing            Storage
```

### Data Flow

1. **Subscribe** to configured MQTT topics
2. **Receive** messages with metadata (QoS, retained flag, user properties)
3. **Parse** payload as UTF-8 text when possible, store raw bytes always
4. **Store** in SQL Server with full message context and timestamps

## 📋 Requirements

### Runtime Requirements

- .NET 8.0 Runtime
- MQTT Broker (Mosquitto, AWS IoT, Azure IoT Hub, etc.)
- SQL Server (2019+, Azure SQL, SQL Express)

### Development Requirements

- .NET 8.0 SDK
- Docker (for containerization)
- Git

## 🔧 Configuration

All configuration is done via environment variables:

### MQTT Settings

| Variable            | Description                | Default                      | Required |
| ------------------- | -------------------------- | ---------------------------- | -------- |
| `MQTT__BROKER_HOST` | MQTT broker hostname/IP    | `localhost`                  | ✅       |
| `MQTT__BROKER_PORT` | MQTT broker port           | `1883`                       | ❌       |
| `MQTT__CLIENT_ID`   | Unique client identifier   | `mqtt-sql-logger-{hostname}` | ❌       |
| `MQTT__USERNAME`    | MQTT username              | -                            | ❌       |
| `MQTT__PASSWORD`    | MQTT password              | -                            | ❌       |
| `MQTT__TOPICS`      | Comma-separated topic list | `#`                          | ❌       |
| `MQTT__QOS`         | Quality of Service (0,1,2) | `1`                          | ❌       |

### SQL Server Settings

| Variable                 | Description                  | Default                | Required |
| ------------------------ | ---------------------------- | ---------------------- | -------- |
| `SQL__CONNECTION_STRING` | SQL Server connection string | Windows Auth localhost | ✅       |
| `SQL__CREATE_TABLE`      | Auto-create table if missing | `true`                 | ❌       |

### Startup Settings

| Variable            | Description                   | Default | Required |
| ------------------- | ----------------------------- | ------- | -------- |
| `STARTUP__DELAY_MS` | Startup delay in milliseconds | `0`     | ❌       |

### Logging Settings

| Variable     | Description       | Default       | Required |
| ------------ | ----------------- | ------------- | -------- |
| `LOG__LEVEL` | Minimum log level | `Information` | ❌       |

## 🚀 Quick Start

### Option 1: Docker (Recommended)

1. **Clone the repository:**

   ```bash
   git clone <your-repo-url>
   cd mqtt-sql-logger
   ```

2. **Build Docker image:**

   ```bash
   docker build -t mqtt-sql-logger:latest .
   ```

3. **Run with Docker:**
   ```bash
   docker run -d \
     --name mqtt-sql-logger \
     --restart unless-stopped \
     -e MQTT__BROKER_HOST="your-broker.com" \
     -e MQTT__USERNAME="your-username" \
     -e MQTT__PASSWORD="your-password" \
     -e MQTT__TOPICS="tele/#,stat/#" \
     -e SQL__CONNECTION_STRING="Server=your-server;Database=MqttLogs;User Id=user;Password=pass;TrustServerCertificate=True;" \
     mqtt-sql-logger:latest
   ```

### Option 2: .NET Runtime

1. **Clone and build:**

   ```bash
   git clone <your-repo-url>
   cd mqtt-sql-logger
   dotnet build -c Release
   ```

2. **Set environment variables:**

   ```bash
   export MQTT__BROKER_HOST="your-broker.com"
   export SQL__CONNECTION_STRING="your-connection-string"
   # ... other variables
   ```

3. **Run:**
   ```bash
   dotnet run --project MqttSqlLogger
   ```

### Option 3: Docker Compose

1. **Edit docker-compose.yml** with your settings
2. **Run:**
   ```bash
   docker-compose up -d
   ```

## 🐳 Docker Deployment

### Portainer Deployment

#### Method 1: Containers

1. **Image:** `mqtt-sql-logger:latest`
2. **Restart policy:** `unless-stopped`
3. **Environment variables:** Add all required MQTT and SQL settings

#### Method 2: Stacks

1. Use the provided `docker-compose.yml`
2. Customize environment variables
3. Deploy stack

For detailed Docker instructions, see [README-Docker.md](README-Docker.md).

## 🗄️ Database Schema

The application creates this table structure:

```sql
CREATE TABLE [dbo].[tblMqttMessageLog](
    [ID] INT IDENTITY(1,1) PRIMARY KEY,
    [ReceivedAt] DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    [Topic] NVARCHAR(256) NOT NULL,
    [QoS] TINYINT NOT NULL,
    [Retained] BIT NOT NULL,
    [ClientId] NVARCHAR(128) NULL,
    [PayloadText] NVARCHAR(MAX) NULL,    -- UTF-8 decoded payload
    [PayloadBytes] VARBINARY(MAX) NULL,  -- Raw message bytes
    [UserPropertiesJson] NVARCHAR(MAX) NULL
);

-- Optimized indexes for querying
CREATE INDEX IX_tblMqttMessageLog_ReceivedAt
  ON [dbo].[tblMqttMessageLog]([ReceivedAt] DESC) INCLUDE([Topic]);
CREATE INDEX IX_tblMqttMessageLog_Topic_ReceivedAt
  ON [dbo].[tblMqttMessageLog]([Topic], [ReceivedAt] DESC);
```

## 📊 Example Use Cases

### Home Automation (Home Assistant, Tasmota)

```bash
MQTT__BROKER_HOST="homeassistant.local"
MQTT__TOPICS="homeassistant/#,tele/#,stat/#"
```

### IoT Device Monitoring

```bash
MQTT__BROKER_HOST="iot-broker.company.com"
MQTT__TOPICS="sensors/#,devices/#,alerts/#"
MQTT__QOS="2"  # Ensure no message loss
```

### Industrial MQTT Logging

```bash
MQTT__BROKER_HOST="scada-mqtt.factory.com"
MQTT__TOPICS="production/#,maintenance/#"
SQL__CONNECTION_STRING="Server=prod-sql;Database=IndustrialLogs;Integrated Security=true;"
```

## 🔐 Security Best Practices

### Docker Security

- ✅ Runs as non-root user (`appuser`)
- ✅ Minimal attack surface
- ✅ No unnecessary ports exposed
- ✅ Environment variable configuration

### Network Security

- Use TLS for MQTT (port 8883)
- Use encrypted SQL connections
- Consider VPN or private networks
- Firewall rules for minimal access

### Credential Management

- Use Docker secrets or Portainer secrets
- Rotate credentials regularly
- Use dedicated service accounts
- Never commit credentials to code

## 📈 Monitoring & Troubleshooting

### Health Monitoring

- Docker health checks included
- Application logs structured events
- Message counters every 100 messages
- Automatic reconnection logging

### Common Issues

**MQTT Connection Failed**

```bash
# Check logs
docker logs mqtt-sql-logger

# Verify connectivity
telnet your-broker.com 1883
```

**SQL Connection Failed**

```bash
# Test connection string
sqlcmd -S your-server -d MqttLogs -U user -P password
```

**No Messages Received**

- Verify topic subscriptions match published topics
- Check MQTT broker allows your client
- Verify QoS levels match expectations

## 🏗️ Development

### Building Locally

```bash
git clone <repo-url>
cd mqtt-sql-logger
dotnet restore
dotnet build
dotnet test  # If tests exist
```

### Docker Build

```bash
# Simple build
docker build -t mqtt-sql-logger .

# Multi-architecture build
docker buildx build --platform linux/amd64,linux/arm64 -t mqtt-sql-logger:multiarch .
```

### Contributing

1. Fork the repository
2. Create feature branch
3. Make changes with tests
4. Submit pull request

## 📄 License

[Add your license here]

## 🆘 Support

- **Issues**: GitHub Issues
- **Documentation**: See [README-Docker.md](README-Docker.md) for Docker details
- **Examples**: Check `docker-compose.yml` for configuration examples

## 🔄 Changelog

### v1.0.0

- Initial release
- MQTT subscription and SQL logging
- Docker containerization
- Portainer support
- Automatic reconnection
- Health monitoring
