# Environment Variables Setup Guide

This guide explains how to securely configure the MQTT SQL Logger using environment variables.

## 🔐 Security Best Practices

✅ **DO**:
- Use `.env` files for sensitive data
- Keep `.env` files out of version control (already in `.gitignore`)
- Use different `.env` files for different environments

❌ **DON'T**:
- Put passwords directly in docker-compose files
- Commit `.env` files to git
- Share `.env` files in chat/email

## 🚀 Quick Setup

### 1. Create Your Environment File

```bash
# Copy the example file
cp .env.example .env

# Edit with your actual values
nano .env
```

### 2. Fill in Your Credentials

Edit `.env` with your actual values:

```bash
# Your MQTT broker
MQTT_BROKER_HOST=192.168.1.100
MQTT_USERNAME=your-mqtt-user
MQTT_PASSWORD=your-mqtt-password

# Your SQL Server
SQL_CONNECTION_STRING=Server=192.168.1.200;Database=MqttLogs;User Id=mqttuser;Password=your-sql-password;TrustServerCertificate=True;
```

### 3. Deploy

```bash
# Docker Compose automatically loads .env file
docker-compose -f docker-compose.simple.yml up -d
```

## 📁 File Structure

```
mqtt-sql-logger/
├── .env.example          # Template file (safe to commit)
├── .env                  # Your actual secrets (never commit!)
├── docker-compose.simple.yml
└── .gitignore            # Contains .env (protects your secrets)
```

## 🔧 Environment Variables Reference

| Variable | Description | Example | Required |
|----------|-------------|---------|----------|
| `MQTT_BROKER_HOST` | MQTT broker hostname/IP | `192.168.1.100` | ✅ |
| `MQTT_BROKER_PORT` | MQTT broker port | `1883` | ❌ |
| `MQTT_CLIENT_ID` | Unique client identifier | `mqtt-logger-prod` | ❌ |
| `MQTT_USERNAME` | MQTT authentication | `mqtt-user` | ❌ |
| `MQTT_PASSWORD` | MQTT password | `secret123` | ❌ |
| `MQTT_TOPICS` | Topics to subscribe to | `sensors/#,devices/#` | ❌ |
| `MQTT_QOS` | Quality of Service (0,1,2) | `1` | ❌ |
| `SQL_CONNECTION_STRING` | SQL Server connection | `Server=...;User Id=...` | ✅ |
| `SQL_CREATE_TABLE` | Auto-create table | `true` | ❌ |
| `LOG_LEVEL` | Logging level | `Information` | ❌ |

## 📋 Connection String Examples

### Local SQL Server (Docker Desktop)
```bash
SQL_CONNECTION_STRING=Server=host.docker.internal;Database=MqttLogs;User Id=sa;Password=YourStrong@Passw0rd123;TrustServerCertificate=True;
```

### Remote SQL Server
```bash
SQL_CONNECTION_STRING=Server=sql.example.com,1433;Database=MqttLogs;User Id=mqttuser;Password=your-password;TrustServerCertificate=True;
```

### Azure SQL Database
```bash
SQL_CONNECTION_STRING=Server=tcp:yourserver.database.windows.net,1433;Initial Catalog=MqttLogs;User ID=yourusername;Password=yourpassword;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

### SQL Server with Windows Authentication (if supported)
```bash
SQL_CONNECTION_STRING=Server=your-server;Database=MqttLogs;Integrated Security=true;TrustServerCertificate=True;
```

## 🧪 Testing Configuration

### Test MQTT Connection
```bash
# Install mosquitto-clients
brew install mosquitto  # macOS
apt-get install mosquitto-clients  # Ubuntu

# Test connection
mosquitto_pub -h YOUR_MQTT_HOST -p 1883 -u YOUR_USERNAME -P YOUR_PASSWORD -t test -m "hello"
```

### Test SQL Connection
```bash
# Install sqlcmd (if not already installed)
# Then test connection
sqlcmd -S YOUR_SQL_SERVER -U YOUR_USERNAME -P YOUR_PASSWORD -Q "SELECT 1"
```

## 🔄 Multiple Environments

Create different `.env` files for different environments:

```bash
.env.development
.env.staging  
.env.production
```

Use specific files:
```bash
# Development
docker-compose --env-file .env.development up -d

# Production
docker-compose --env-file .env.production up -d
```

## 🆘 Troubleshooting

### Environment Variables Not Loading
```bash
# Check if .env file exists
ls -la .env

# Check if Docker Compose finds the file
docker-compose config
```

### Values Not Substituted
```bash
# Verify environment variables are set
docker-compose config | grep -E "(MQTT|SQL)__"

# Check for syntax errors in .env
cat .env
```

### Still Using Placeholder Values
Make sure your `.env` file has actual values, not the examples:
```bash
# ❌ Wrong (still using example)
MQTT_BROKER_HOST=mqtt.example.com

# ✅ Correct (real hostname)
MQTT_BROKER_HOST=192.168.1.100
```

## 🔒 Security Notes

- The `.env` file is already in `.gitignore` - it won't be committed
- Never share your `.env` file
- Use strong passwords for SQL Server
- Consider using Docker secrets for production environments
- Regularly rotate credentials