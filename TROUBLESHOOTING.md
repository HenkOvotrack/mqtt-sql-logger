# MQTT SQL Logger - Troubleshooting Guide

## 🚨 Common Issues and Solutions

### Issue 1: SQL Server Connection Errors
```
Microsoft.Data.SqlClient.SqlException: A network-related or instance-specific error occurred while establishing a connection to SQL Server
```

**Causes:**
- ❌ Using placeholder connection string (`your-sql-server.example.com`)
- ❌ SQL Server not accessible from container
- ❌ Wrong credentials
- ❌ Firewall blocking port 1433

**Solutions:**
✅ **Use .env file for secure configuration** (Recommended):
```bash
# 1. Copy the template
cp .env.example .env

# 2. Edit with your actual values
nano .env

# 3. Deploy
docker-compose -f docker-compose.simple.yml up -d
```

Your `.env` file should contain:
```bash
SQL_CONNECTION_STRING=Server=YOUR_REAL_SERVER;Database=MqttLogs;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;
```

✅ **For local SQL Server** (running on host machine):
```bash
# In your .env file:
# On Windows/Mac Docker Desktop, use host.docker.internal
SQL_CONNECTION_STRING=Server=host.docker.internal;Database=MqttLogs;User Id=sa;Password=YourPassword;TrustServerCertificate=True;

# On Linux, use the host IP address
SQL_CONNECTION_STRING=Server=192.168.1.100;Database=MqttLogs;User Id=sa;Password=YourPassword;TrustServerCertificate=True;
```

✅ **Test SQL connectivity** before running the container:
```bash
# Test connection from your host machine first
sqlcmd -S your-server -U your-user -P your-password -Q "SELECT 1"
```

### Issue 2: MQTT Broker Connection Errors
```
MQTTnet.Exceptions.MqttCommunicationException: Error while connecting with host 'localhost:1883'
System.Net.Sockets.SocketException (111): Connection refused
```

**Causes:**
- ❌ Using `localhost` (refers to inside the container, not your host)
- ❌ MQTT broker not running
- ❌ Wrong hostname/IP address
- ❌ Firewall blocking port 1883

**Solutions:**
✅ **Use .env file for secure configuration** (Recommended):
```bash
# In your .env file:
# For external MQTT broker
MQTT_BROKER_HOST=mqtt.your-domain.com

# For MQTT broker on host machine (Docker Desktop)
MQTT_BROKER_HOST=host.docker.internal

# For MQTT broker on Linux host
MQTT_BROKER_HOST=192.168.1.100

# Add credentials if needed
MQTT_USERNAME=your-mqtt-user
MQTT_PASSWORD=your-mqtt-password
```

✅ **Test MQTT connectivity** before running the container:
```bash
# Install mosquitto-clients to test
mosquitto_pub -h your-broker -t test -m "hello"
mosquitto_sub -h your-broker -t test
```

### Issue 3: Container Keeps Restarting
```
BackgroundService failed ... The IHost instance is stopping
```

**Cause:** Application crashes due to connection failures and doesn't restart automatically.

**Solution:** ✅ Fix the connection issues above, then:
```bash
# Remove the failed container
docker rm mqtt-sql-logger

# Start with corrected configuration
docker-compose up -d
```

## 🧪 Testing with Working Example

Use our `docker-compose.working-example.yml` for a complete working setup:

```bash
# Start the complete stack (MQTT + SQL + Logger)
docker-compose -f docker-compose.working-example.yml up -d

# Check all services are healthy
docker-compose -f docker-compose.working-example.yml ps

# View logs
docker-compose -f docker-compose.working-example.yml logs -f mqtt-sql-logger

# Test by publishing a message
docker exec mosquitto-test mosquitto_pub -h localhost -t test/sensor1 -m '{"temperature": 22.5, "humidity": 45}'

# Check database
docker exec sqlserver-test /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Passw0rd123' -Q "SELECT TOP 5 * FROM MqttLogs.dbo.tblMqttMessageLog ORDER BY ReceivedAt DESC"
```

## 🔧 Configuration Checklist

Before deploying, ensure you have:

- [ ] **Real MQTT broker** hostname/IP (not `localhost` or placeholder)
- [ ] **Working SQL Server** connection string (test it first)
- [ ] **Network connectivity** between container and services
- [ ] **Correct credentials** for both MQTT and SQL
- [ ] **Firewall rules** allow ports 1883 (MQTT) and 1433 (SQL)

## 📋 Quick Fixes

### Fix 1: Use Environment Variables (Recommended)
```bash
# 1. Copy the template
cp .env.example .env

# 2. Edit with your actual credentials
nano .env

# 3. Deploy
docker-compose -f docker-compose.simple.yml up -d
```

### Fix 2: Use Working Example
Deploy the complete working stack:
```bash
docker-compose -f docker-compose.working-example.yml up -d
```

### Fix 3: Check Container Logs
Always check logs for specific error messages:
```bash
docker logs mqtt-sql-logger-test -f
```

## 🆘 Still Having Issues?

1. **Check network connectivity:**
   ```bash
   # Test from inside container
   docker exec mqtt-sql-logger ping your-mqtt-broker
   docker exec mqtt-sql-logger nc -zv your-sql-server 1433
   ```

2. **Verify environment variables:**
   ```bash
   docker exec mqtt-sql-logger env | grep -E "(MQTT|SQL)__"
   ```

3. **Test services independently:**
   - Test MQTT broker with `mosquitto_pub/sub`
   - Test SQL Server with `sqlcmd`

Remember: The application is working correctly - the issue is with the configuration pointing to non-existent services! 🎯