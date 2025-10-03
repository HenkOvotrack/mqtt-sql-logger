# MQTT SQL Logger - Docker Deployment Guide

This guide explains how to build and deploy the MQTT SQL Logger application as a Docker container, particularly for use with Portainer.

## Quick Start

### 1. Build the Docker Image

```bash
# Navigate to the project directory
cd /Users/henkbeekhuis/source/test/mqttlogger

# Build the Docker image
docker build -t mqtt-sql-logger:latest .

# Or build with a specific tag
docker build -t mqtt-sql-logger:v1.0.0 .
```

### 2. Run with Docker

```bash
docker run -d \
  --name mqtt-sql-logger \
  --restart unless-stopped \
  -e MQTT__BROKER_HOST="your-mqtt-broker.example.com" \
  -e MQTT__BROKER_PORT="1883" \
  -e MQTT__USERNAME="your-username" \
  -e MQTT__PASSWORD="your-password" \
  -e MQTT__TOPICS="tele/#,stat/#" \
  -e SQL__CONNECTION_STRING="Server=your-sql-server;Database=MqttLogs;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;" \
  -e SQL__CREATE_TABLE="true" \
  -e STARTUP__DELAY_MS="5000" \
  -e LOG__LEVEL="Information" \
  mqtt-sql-logger:latest
```

### 3. Run with Docker Compose

```bash
# Edit docker-compose.yml with your configuration
nano docker-compose.yml

# Start the services
docker-compose up -d

# View logs
docker-compose logs -f mqtt-sql-logger

# Stop the services
docker-compose down
```

## Portainer Deployment

### Option 1: Using Portainer Stacks (Docker Compose)

1. **Log into Portainer**
2. **Navigate to Stacks**
3. **Click "Add Stack"**
4. **Name your stack** (e.g., "mqtt-sql-logger")
5. **Copy and paste the docker-compose.yml content**
6. **Modify environment variables** for your setup
7. **Deploy the stack**

### Option 2: Using Portainer Containers

1. **Log into Portainer**
2. **Navigate to Containers**
3. **Click "Add Container"**
4. **Configure the container:**

   - **Name:** `mqtt-sql-logger`
   - **Image:** `mqtt-sql-logger:latest` (or your image name)
   - **Restart Policy:** `unless-stopped`

5. **Add Environment Variables:**

   ```
   MQTT__BROKER_HOST=your-mqtt-broker.example.com
   MQTT__BROKER_PORT=1883
   MQTT__CLIENT_ID=mqtt-sql-logger-portainer
   MQTT__USERNAME=your-mqtt-username
   MQTT__PASSWORD=your-mqtt-password
   MQTT__TOPICS=tele/#,stat/#,shellies/#
   MQTT__QOS=1
   SQL__CONNECTION_STRING=Server=your-sql-server;Database=MqttLogs;User Id=your-username;Password=your-password;TrustServerCertificate=True;
   SQL__CREATE_TABLE=true
   STARTUP__DELAY_MS=5000
   LOG__LEVEL=Information
   ```

6. **Deploy the container**

## Environment Variables Reference

| Variable                 | Description                           | Default                  | Required |
| ------------------------ | ------------------------------------- | ------------------------ | -------- |
| `MQTT__BROKER_HOST`      | MQTT broker hostname or IP            | `localhost`              | Yes      |
| `MQTT__BROKER_PORT`      | MQTT broker port                      | `1883`                   | No       |
| `MQTT__CLIENT_ID`        | MQTT client identifier                | `mqtt-sql-logger-docker` | No       |
| `MQTT__USERNAME`         | MQTT username                         | (empty)                  | No       |
| `MQTT__PASSWORD`         | MQTT password                         | (empty)                  | No       |
| `MQTT__TOPICS`           | Comma-separated list of topics        | `#`                      | No       |
| `MQTT__QOS`              | MQTT Quality of Service level (0,1,2) | `1`                      | No       |
| `SQL__CONNECTION_STRING` | SQL Server connection string          | See default              | Yes      |
| `SQL__CREATE_TABLE`      | Create table if not exists            | `true`                   | No       |
| `STARTUP__DELAY_MS`      | Startup delay in milliseconds         | `0`                      | No       |
| `LOG__LEVEL`             | Logging level                         | `Information`            | No       |

## Security Best Practices

### 1. Secrets Management in Portainer

- Use Portainer's **Secrets** feature for sensitive data
- Never put passwords directly in docker-compose files
- Use environment variables or Docker secrets

### 2. Network Security

```yaml
# Example with custom network in docker-compose.yml
networks:
  mqtt-network:
    driver: bridge
    internal: true # Prevents external access
```

### 3. User Security

The Docker image runs as a non-root user (`appuser`) for security.

## Monitoring and Troubleshooting

### View Logs

```bash
# Docker logs
docker logs mqtt-sql-logger -f

# Docker Compose logs
docker-compose logs mqtt-sql-logger -f

# In Portainer: Navigate to Containers > mqtt-sql-logger > Logs
```

### Health Checks

The container includes health checks that monitor if the application is running:

```bash
# Check container health
docker inspect mqtt-sql-logger | grep -A 10 Health

# In Portainer: The container status will show as "healthy" or "unhealthy"
```

### Common Issues

1. **Cannot connect to MQTT broker**

   - Check `MQTT__BROKER_HOST` and `MQTT__BROKER_PORT`
   - Verify network connectivity
   - Check firewall settings

2. **Cannot connect to SQL Server**

   - Verify `SQL__CONNECTION_STRING`
   - Ensure SQL Server accepts connections
   - Check if database exists

3. **Authentication failures**
   - Verify `MQTT__USERNAME` and `MQTT__PASSWORD`
   - Check SQL Server credentials in connection string

## Building and Publishing

### Build for Different Architectures

```bash
# Build for AMD64 (x86_64)
docker build --platform linux/amd64 -t mqtt-sql-logger:amd64 .

# Build for ARM64 (e.g., Raspberry Pi 4)
docker build --platform linux/arm64 -t mqtt-sql-logger:arm64 .

# Multi-platform build
docker buildx build --platform linux/amd64,linux/arm64 -t mqtt-sql-logger:multiarch .
```

### Push to Docker Registry

```bash
# Tag for Docker Hub
docker tag mqtt-sql-logger:latest yourusername/mqtt-sql-logger:latest

# Push to Docker Hub
docker push yourusername/mqtt-sql-logger:latest

# Use in Portainer with: yourusername/mqtt-sql-logger:latest
```

## Example Configurations

### For Home Assistant MQTT

```yaml
environment:
  MQTT__BROKER_HOST: "homeassistant.local"
  MQTT__BROKER_PORT: "1883"
  MQTT__USERNAME: "ha-user"
  MQTT__PASSWORD: "ha-password"
  MQTT__TOPICS: "homeassistant/#,tele/#"
```

### For Tasmota Devices

```yaml
environment:
  MQTT__BROKER_HOST: "mqtt.local"
  MQTT__TOPICS: "tele/#,stat/#,cmnd/#"
  MQTT__QOS: "1"
```

### For Production with TLS

```yaml
environment:
  MQTT__BROKER_HOST: "mqtt.production.com"
  MQTT__BROKER_PORT: "8883" # TLS port
  SQL__CONNECTION_STRING: "Server=sql.production.com;Database=MqttLogs;User Id=mqtt_user;Password=SecurePassword123;Encrypt=True;"
```

## Performance Considerations

- **High throughput**: Consider connection pooling for SQL Server
- **Large messages**: Monitor memory usage
- **Network issues**: The app includes automatic reconnection with backoff

For questions or issues, check the container logs first, then verify network connectivity and credentials.
