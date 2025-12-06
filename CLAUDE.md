# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MQTT SQL Logger is a .NET 8 application that subscribes to MQTT topics and persists messages to SQL Server. It's designed for IoT data collection and industrial MQTT message persistence.

## Build & Run Commands

```bash
# Build
dotnet build -c Release

# Run locally (requires environment variables)
dotnet run --project MqttSqlLogger

# Docker build
docker build -t mqtt-sql-logger:latest .

# Docker run
docker run -d --name mqtt-sql-logger --restart unless-stopped \
  -e MQTT__BROKER_HOST="broker.example.com" \
  -e SQL__CONNECTION_STRING="Server=sql;Database=MqttLogs;..." \
  mqtt-sql-logger:latest
```

## Architecture

Single-file application (`MqttSqlLogger/Program.cs`) using .NET Generic Host pattern:

- **AppSettings record**: Immutable configuration parsed from environment variables
- **MqttSqlLoggerService**: BackgroundService that manages MQTT connection lifecycle and SQL persistence
- **Connection resilience**: Exponential backoff with jitter for both initial connection and reconnection
- **Concurrency**: SemaphoreSlim for reconnection locking, async message processing

Data flow: MQTT Broker → MqttSqlLoggerService → SQL Server (`tblMqttMessageLog`)

## Configuration

All configuration via environment variables with `__` (double underscore) as hierarchy separator:

| Variable | Purpose |
|----------|---------|
| `MQTT__BROKER_HOST` | MQTT broker hostname (required) |
| `MQTT__BROKER_PORT` | Broker port (default: 1883) |
| `MQTT__CLIENT_ID` | Client identifier |
| `MQTT__TOPICS` | Comma-separated topic list (default: `#`) |
| `MQTT__QOS` | Quality of Service 0-2 (default: 1) |
| `SQL__CONNECTION_STRING` | SQL Server connection (required) |
| `SQL__CREATE_TABLE` | Auto-create table (default: true) |
| `STARTUP__DELAY_MS` | Startup delay for dependencies |

## Key Dependencies

- MQTTnet 4.3.x - MQTT client
- Microsoft.Data.SqlClient - SQL Server connectivity
- Microsoft.Extensions.Hosting - Generic host pattern

## Database

Table `[dbo].[tblMqttMessageLog]` auto-created with:
- Topic, QoS, Retained flag, ClientId
- PayloadText (UTF-8 decoded) and PayloadBytes (raw)
- UserPropertiesJson (MQTT 5 properties)
- Indexes on ReceivedAt and Topic for query performance
