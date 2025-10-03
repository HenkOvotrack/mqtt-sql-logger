# VSCode Debugging Guide for MQTT SQL Logger

This guide helps you debug the MQTT SQL Logger application in VSCode with proper environment variable configuration.

## 🚀 Quick Start

### 1. **Open in VSCode**

```bash
code /Users/henkbeekhuis/source/test/mqttlogger
```

### 2. **Choose Your Debugging Scenario**

I've created 3 debug configurations for different scenarios:

| Configuration                | Use Case                           | Services Needed   |
| ---------------------------- | ---------------------------------- | ----------------- |
| **Debug MQTT SQL Logger**    | Uses your `.env` file              | Your own MQTT/SQL |
| **Debug (Test Environment)** | Local SQL Server with Windows Auth | Local SQL Server  |
| **Debug (Docker Services)**  | Uses Docker services               | Docker MQTT + SQL |

## 🔧 Setup Instructions

### Option A: Using Your .env File (Recommended)

1. **Create/Update your .env file:**

   ```bash
   cp .env.example .env
   nano .env
   ```

2. **Set your actual credentials in .env**

3. **Select debug configuration:**
   - Go to **Run and Debug** (Ctrl+Shift+D)
   - Select **"Debug MQTT SQL Logger"**
   - Press **F5** to start debugging

### Option B: Using Docker Services (Easy Testing)

1. **Start Docker services:**

   ```bash
   # From VSCode Terminal or external terminal
   docker-compose -f docker-compose.working-example.yml up -d mosquitto sqlserver
   ```

   Or use the VSCode task:

   - **Ctrl+Shift+P** → **"Tasks: Run Task"**
   - Select **"Start Docker Services for Debugging"**

2. **Select debug configuration:**

   - Go to **Run and Debug** (Ctrl+Shift+D)
   - Select **"Debug MQTT SQL Logger (Docker Services)"**
   - Press **F5** to start debugging

3. **Test by sending MQTT messages:**
   ```bash
   # In terminal
   docker exec mosquitto-test mosquitto_pub -h localhost -t test/sensor1 -m '{"temperature": 22.5}'
   ```

### Option C: Local SQL Server

1. **Ensure SQL Server is running locally**

2. **Select debug configuration:**
   - Go to **Run and Debug** (Ctrl+Shift+D)
   - Select **"Debug MQTT SQL Logger (Test Environment)"**
   - Press **F5** to start debugging

## 🐛 Debugging Features

### Breakpoints

Set breakpoints in key locations:

- **Line 130**: `ExecuteAsync` method start
- **Line 196**: MQTT connection attempt
- **Line 264**: SQL table creation
- **Line 299**: Message received handler
- **Line 308**: Database insert

### Debug Console

Monitor these variables:

- `mqttOptions`: MQTT connection settings
- `connectionString`: SQL connection string
- `receivedMessage`: Incoming MQTT messages
- `insertedId`: Database insert results

### Logging

The debug configuration uses `LOG__LEVEL=Debug` for detailed output:

```
dbug: MqttSqlLoggerService[0] Attempting to connect to MQTT broker...
dbug: MqttSqlLoggerService[0] SQL connection string: Server=localhost...
dbug: MqttSqlLoggerService[0] Subscribed to topics: test/#,debug/#
```

## 🧪 Testing Your Debug Session

### 1. **Send Test MQTT Messages**

**If using Docker services:**

```bash
# Temperature sensor data
docker exec mosquitto-test mosquitto_pub -h localhost -t test/temperature -m '{"value": 23.5, "unit": "C"}'

# Device status
docker exec mosquitto-test mosquitto_pub -h localhost -t debug/device1 -m '{"status": "online", "battery": 85}'
```

**If using external MQTT broker:**

```bash
# Replace with your broker details
mosquitto_pub -h YOUR_MQTT_HOST -u YOUR_USERNAME -P YOUR_PASSWORD -t test/sensor -m '{"data": "test"}'
```

### 2. **Verify Database Inserts**

**If using Docker SQL Server:**

```bash
docker exec sqlserver-test /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Passw0rd123' -Q "SELECT TOP 5 * FROM MqttLogs.dbo.tblMqttMessageLog ORDER BY ReceivedAt DESC"
```

**If using local SQL Server:**

```bash
sqlcmd -S localhost -E -Q "SELECT TOP 5 * FROM MqttLogs.dbo.tblMqttMessageLog ORDER BY ReceivedAt DESC"
```

## 🔧 Troubleshooting Debug Issues

### Issue: "Cannot find .env file"

**Solution:** Create the .env file:

```bash
cp .env.example .env
# Edit with your actual values
```

### Issue: "SQL Server connection failed"

**Solutions:**

1. **Check if SQL Server is running:**

   ```bash
   # For Docker
   docker ps | grep sqlserver

   # For local SQL Server
   services.msc  # Windows
   brew services list | grep sql  # macOS
   ```

2. **Test connection manually:**
   ```bash
   sqlcmd -S localhost -U sa -P 'YourPassword' -Q "SELECT 1"
   ```

### Issue: "MQTT connection refused"

**Solutions:**

1. **Check if MQTT broker is running:**

   ```bash
   # For Docker
   docker ps | grep mosquitto

   # Test connection
   mosquitto_pub -h localhost -t test -m "hello"
   ```

2. **Verify MQTT configuration in debug config**

### Issue: "Build failed"

**Solution:**

```bash
# Clean and rebuild
dotnet clean MqttSqlLogger/MqttSqlLogger.csproj
dotnet build MqttSqlLogger/MqttSqlLogger.csproj
```

## 📁 VSCode Files Created

| File                    | Purpose                           |
| ----------------------- | --------------------------------- |
| `.vscode/launch.json`   | Debug configurations              |
| `.vscode/tasks.json`    | Build and Docker tasks            |
| `.vscode/settings.json` | C# and .NET settings              |
| `.env.development`      | Development environment variables |

## 🎯 Debug Configuration Details

### "Debug MQTT SQL Logger"

- Uses your `.env` file
- Best for production-like debugging
- Loads environment variables automatically

### "Debug (Test Environment)"

- Hardcoded test values
- Uses Windows Authentication for SQL
- Good for quick local testing

### "Debug (Docker Services)"

- Points to Docker containers
- Easy setup with working-example.yml
- Consistent environment

## 🚀 Advanced Debugging

### Environment Variable Override

You can override specific environment variables in `launch.json`:

```json
"env": {
    "LOG__LEVEL": "Trace",  // Override log level
    "MQTT__TOPICS": "debug/#"  // Override topics
}
```

### Conditional Breakpoints

Set breakpoints that only trigger on specific conditions:

- Right-click breakpoint → "Edit Breakpoint"
- Add condition: `topic.Contains("error")`

### Debug Console Commands

Use the debug console to evaluate expressions:

```
connectionString
mqttOptions.ClientId
receivedMessage.Topic
```

Happy debugging! 🐛✨
