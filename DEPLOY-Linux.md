# Linux Deployment Guide for MQTT SQL Logger

This guide provides step-by-step instructions for deploying the MQTT SQL Logger on a Linux system with Docker.

## 📋 Prerequisites

### System Requirements
- **Linux Distribution**: Ubuntu 20.04+, Debian 11+, CentOS 8+, or RHEL 8+
- **Architecture**: x86_64 (AMD64) or ARM64
- **Memory**: Minimum 512MB RAM, recommended 1GB+
- **Disk Space**: 2GB free space for Docker images and logs
- **Network**: Outbound access to MQTT broker and SQL Server

### Required Software
- **Git**: For cloning the repository
- **Docker**: 20.10+ (Docker Engine or Docker Desktop)
- **Optional**: docker-compose for stack deployment

## 🚀 Step-by-Step Deployment

### Step 1: Install Docker (if not already installed)

#### Ubuntu/Debian:
```bash
# Update package list
sudo apt-get update

# Install required packages
sudo apt-get install -y apt-transport-https ca-certificates curl gnupg lsb-release

# Add Docker's official GPG key
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg

# Add Docker repository
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin

# Start Docker service
sudo systemctl start docker
sudo systemctl enable docker

# Add current user to docker group (logout/login required)
sudo usermod -aG docker $USER
```

#### CentOS/RHEL:
```bash
# Install Docker
sudo yum install -y yum-utils
sudo yum-config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
sudo yum install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin

# Start Docker service
sudo systemctl start docker
sudo systemctl enable docker

# Add current user to docker group
sudo usermod -aG docker $USER
```

#### Verify Docker Installation:
```bash
# Check Docker version
docker --version

# Test Docker (may require logout/login after adding to docker group)
docker run hello-world
```

### Step 2: Clone the Repository

```bash
# Clone the repository (replace with your actual repo URL)
git clone https://github.com/yourusername/mqtt-sql-logger.git

# Navigate to the project directory
cd mqtt-sql-logger

# Verify files are present
ls -la
```

Expected files:
```
├── Dockerfile
├── .dockerignore
├── .gitignore
├── README.md
├── README-Docker.md
├── docker-compose.yml
├── build-linux.sh
├── mqttlogger.sln
└── MqttSqlLogger/
    ├── MqttSqlLogger.csproj
    └── Program.cs
```

### Step 3: Build the Docker Image

```bash
# Make the build script executable (if not already)
chmod +x build-linux.sh

# Run the build script
./build-linux.sh

# Or build manually
docker build -t mqtt-sql-logger:latest .
```

The build process will:
1. Download .NET 8 SDK and runtime images
2. Restore NuGet packages
3. Compile the application
4. Create optimized runtime image
5. Run basic tests

### Step 4: Configure Environment Variables

Create a configuration file for your environment:

```bash
# Create environment file (don't commit this to git!)
cat > .env << 'EOF'
# MQTT Configuration
MQTT__BROKER_HOST=your-mqtt-broker.example.com
MQTT__BROKER_PORT=1883
MQTT__CLIENT_ID=mqtt-sql-logger-production
MQTT__USERNAME=your-mqtt-username
MQTT__PASSWORD=your-mqtt-password
MQTT__TOPICS=tele/#,stat/#,homeassistant/#
MQTT__QOS=1

# SQL Server Configuration
SQL__CONNECTION_STRING=Server=your-sql-server.example.com;Database=MqttLogs;User Id=mqtt_logger;Password=YourSecurePassword123;TrustServerCertificate=True;
SQL__CREATE_TABLE=true

# Logging Configuration
LOG__LEVEL=Information
EOF

# Secure the environment file
chmod 600 .env
```

### Step 5: Deploy the Container

#### Option A: Docker Run (Simple)

```bash
# Load environment variables and run
set -a  # Export all variables
source .env
set +a

docker run -d \
  --name mqtt-sql-logger \
  --restart unless-stopped \
  --env-file .env \
  mqtt-sql-logger:latest
```

#### Option B: Docker Compose (Recommended)

```bash
# Edit docker-compose.yml with your settings
nano docker-compose.yml

# Deploy with docker-compose
docker-compose up -d

# View logs
docker-compose logs -f mqtt-sql-logger
```

#### Option C: Systemd Service (Advanced)

Create a systemd service for automatic startup:

```bash
# Create systemd service file
sudo tee /etc/systemd/system/mqtt-sql-logger.service > /dev/null << 'EOF'
[Unit]
Description=MQTT SQL Logger Container
Requires=docker.service
After=docker.service

[Service]
Type=forking
RemainAfterExit=yes
WorkingDirectory=/path/to/mqtt-sql-logger
ExecStart=/usr/bin/docker-compose up -d
ExecStop=/usr/bin/docker-compose down
TimeoutStartSec=0

[Install]
WantedBy=multi-user.target
EOF

# Update the WorkingDirectory path
sudo sed -i 's|/path/to/mqtt-sql-logger|'$(pwd)'|g' /etc/systemd/system/mqtt-sql-logger.service

# Enable and start the service
sudo systemctl daemon-reload
sudo systemctl enable mqtt-sql-logger.service
sudo systemctl start mqtt-sql-logger.service

# Check service status
sudo systemctl status mqtt-sql-logger.service
```

### Step 6: Verify Deployment

```bash
# Check container status
docker ps -a | grep mqtt-sql-logger

# View logs
docker logs mqtt-sql-logger -f

# Check container health
docker inspect mqtt-sql-logger | grep -A 10 '"Health"'

# Test MQTT connectivity (if mosquitto-clients installed)
mosquitto_pub -h your-mqtt-broker.example.com -t test/topic -m "Hello from Linux!"
```

### Step 7: Monitor and Maintain

#### Log Management
```bash
# View real-time logs
docker logs mqtt-sql-logger -f

# View last 100 lines
docker logs mqtt-sql-logger --tail 100

# Export logs for analysis
docker logs mqtt-sql-logger > mqtt-logger.log 2>&1
```

#### Container Management
```bash
# Restart container
docker restart mqtt-sql-logger

# Stop container
docker stop mqtt-sql-logger

# Remove container (data in SQL Server is preserved)
docker rm mqtt-sql-logger

# Update to new version
docker pull mqtt-sql-logger:latest
docker stop mqtt-sql-logger
docker rm mqtt-sql-logger
# Run new container with same configuration
```

#### Resource Monitoring
```bash
# Monitor resource usage
docker stats mqtt-sql-logger

# Check disk usage
docker system df

# Clean up unused images
docker image prune -f
```

## 🔧 Advanced Configuration

### Firewall Configuration
```bash
# Ubuntu/Debian (UFW)
sudo ufw allow out 1883  # MQTT
sudo ufw allow out 1433  # SQL Server
sudo ufw allow out 53    # DNS

# CentOS/RHEL (firewalld)
sudo firewall-cmd --permanent --add-port=1883/tcp
sudo firewall-cmd --permanent --add-port=1433/tcp
sudo firewall-cmd --reload
```

### Network Configuration
```bash
# Create dedicated Docker network
docker network create mqtt-network

# Run container on dedicated network
docker run -d --name mqtt-sql-logger \
  --network mqtt-network \
  --restart unless-stopped \
  --env-file .env \
  mqtt-sql-logger:latest
```

### Storage Configuration
```bash
# Create volume for persistent logs (optional)
docker volume create mqtt-logger-logs

# Run with volume mounted
docker run -d --name mqtt-sql-logger \
  --restart unless-stopped \
  --env-file .env \
  -v mqtt-logger-logs:/app/logs \
  mqtt-sql-logger:latest
```

## 🔒 Security Hardening

### Container Security
```bash
# Run security scan (if Docker Bench installed)
docker run --rm --net host --pid host --userns host --cap-add audit_control \
  -v /etc:/etc:ro \
  -v /usr/bin/systemd-analyze:/usr/bin/systemd-analyze:ro \
  -v /usr/lib/systemd:/usr/lib/systemd:ro \
  -v /var/lib:/var/lib:ro \
  -v /var/run/docker.sock:/var/run/docker.sock:ro \
  --label docker_bench_security \
  docker/docker-bench-security
```

### File Permissions
```bash
# Secure environment files
chmod 600 .env
chmod 600 docker-compose.yml

# Restrict access to project directory
chmod 750 $(pwd)
```

### Network Security
- Use TLS for MQTT connections (port 8883)
- Use encrypted SQL Server connections
- Consider VPN for remote deployments
- Implement proper firewall rules

## 🚨 Troubleshooting

### Common Issues

#### Container Won't Start
```bash
# Check container logs
docker logs mqtt-sql-logger

# Check Docker daemon logs
sudo journalctl -u docker.service

# Verify image exists
docker images mqtt-sql-logger
```

#### MQTT Connection Issues
```bash
# Test MQTT connectivity
telnet your-mqtt-broker.com 1883

# Check firewall rules
sudo iptables -L | grep 1883

# Verify credentials
mosquitto_sub -h your-broker -u username -P password -t test/#
```

#### SQL Server Connection Issues
```bash
# Test SQL connectivity
telnet your-sql-server.com 1433

# Test with sqlcmd (if available)
sqlcmd -S your-server -d MqttLogs -U username -P password -Q "SELECT 1"
```

#### Performance Issues
```bash
# Monitor container resources
docker stats mqtt-sql-logger

# Check host resources
htop
free -h
df -h

# Analyze SQL Server performance
# Use SQL Server Management Studio or Azure Data Studio
```

### Log Analysis
```bash
# Search for specific errors
docker logs mqtt-sql-logger 2>&1 | grep -i error

# Count message processing
docker logs mqtt-sql-logger 2>&1 | grep "Inserted.*messages" | tail -5

# Monitor reconnections
docker logs mqtt-sql-logger 2>&1 | grep -i "reconnect"
```

## 📊 Production Deployment Checklist

- [ ] Docker installed and running
- [ ] Repository cloned and image built successfully
- [ ] Environment variables configured securely
- [ ] MQTT broker connectivity tested
- [ ] SQL Server connectivity tested
- [ ] Container deployed and running
- [ ] Logs showing successful message processing
- [ ] Firewall rules configured
- [ ] Monitoring/alerting configured
- [ ] Backup strategy for SQL Server database
- [ ] Update procedure documented
- [ ] Access credentials secured and documented

## 🔄 Updates and Maintenance

### Updating the Application
```bash
# Pull latest code
git pull origin main

# Rebuild image
./build-linux.sh

# Update running container
docker-compose down
docker-compose up -d

# Verify update
docker logs mqtt-sql-logger -f
```

### Backup Considerations
- **SQL Server**: Regular database backups
- **Configuration**: Backup .env and docker-compose.yml files
- **Image**: Consider backing up the Docker image to a registry

This guide should help you successfully deploy the MQTT SQL Logger on your Linux system. For additional support, refer to the main README.md and README-Docker.md files.