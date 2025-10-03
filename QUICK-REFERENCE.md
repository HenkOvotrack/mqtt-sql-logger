# MQTT SQL Logger - Quick Reference

## 🚀 Quick Deploy on Linux

```bash
# 1. Clone repository
git clone https://github.com/yourusername/mqtt-sql-logger.git
cd mqtt-sql-logger

# 2. Build Docker image
./build-linux.sh

# 3. Configure environment
cat > .env << 'EOF'
MQTT__BROKER_HOST=your-broker.com
MQTT__USERNAME=your-user
MQTT__PASSWORD=your-pass
MQTT__TOPICS=tele/#,stat/#
SQL__CONNECTION_STRING=Server=your-sql;Database=MqttLogs;User Id=user;Password=pass;TrustServerCertificate=True;
EOF

# 4. Deploy
docker run -d --name mqtt-sql-logger --restart unless-stopped --env-file .env mqtt-sql-logger:latest

# 5. Monitor
docker logs mqtt-sql-logger -f
```

## 🔧 Environment Variables

| Variable | Example | Required |
|----------|---------|----------|
| `MQTT__BROKER_HOST` | `mqtt.example.com` | ✅ |
| `MQTT__BROKER_PORT` | `1883` | ❌ |
| `MQTT__USERNAME` | `mqtt-user` | ❌ |
| `MQTT__PASSWORD` | `secret123` | ❌ |
| `MQTT__TOPICS` | `tele/#,stat/#` | ❌ |
| `SQL__CONNECTION_STRING` | `Server=sql;Database=MqttLogs;...` | ✅ |

## 📊 Common Commands

```bash
# View logs
docker logs mqtt-sql-logger -f

# Restart container
docker restart mqtt-sql-logger

# Update container
docker pull mqtt-sql-logger:latest && docker-compose up -d

# Monitor resources
docker stats mqtt-sql-logger

# Database query example
SELECT TOP 100 * FROM tblMqttMessageLog ORDER BY ReceivedAt DESC
```

## 🔒 Portainer Deployment

1. **Containers** → **Add Container**
2. **Image:** `mqtt-sql-logger:latest`
3. **Name:** `mqtt-sql-logger`
4. **Restart:** `unless-stopped`
5. **Environment:** Add MQTT and SQL variables
6. **Deploy**

## 🆘 Troubleshooting

| Issue | Check | Solution |
|-------|-------|----------|
| Container won't start | `docker logs mqtt-sql-logger` | Verify environment variables |
| No MQTT messages | `telnet broker 1883` | Check broker connectivity |
| SQL errors | Connection string | Verify SQL Server access |
| High memory usage | `docker stats` | Check message volume |

## 📁 Project Structure

```
mqtt-sql-logger/
├── Dockerfile                  # Multi-stage Docker build
├── docker-compose.yml          # Stack deployment
├── MqttSqlLogger/
│   ├── Program.cs              # Main application
│   └── MqttSqlLogger.csproj    # Project file
├── README.md                   # Main documentation
├── README-Docker.md            # Docker guide
├── DEPLOY-Linux.md             # Linux deployment
└── build-linux.sh             # Build automation
```