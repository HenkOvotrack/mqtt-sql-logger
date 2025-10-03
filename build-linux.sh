#!/bin/bash

# MQTT SQL Logger - Linux Docker Build Script
# Optimized for Linux systems with Docker support

set -e  # Exit on any error

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
IMAGE_NAME="mqtt-sql-logger"
IMAGE_TAG="${1:-latest}"  # Use first argument or default to 'latest'
FULL_IMAGE_NAME="${IMAGE_NAME}:${IMAGE_TAG}"

echo -e "${BLUE}🚀 MQTT SQL Logger - Linux Docker Build${NC}"
echo -e "${BLUE}===========================================${NC}"
echo ""

# Check if running on Linux
if [[ "$OSTYPE" != "linux-gnu"* ]]; then
    echo -e "${YELLOW}⚠️  Warning: This script is optimized for Linux systems${NC}"
    echo -e "${YELLOW}   Current OS: $OSTYPE${NC}"
    echo ""
fi

# Check if Docker is installed and running
echo -e "${BLUE}🔍 Checking Docker installation...${NC}"
if ! command -v docker &> /dev/null; then
    echo -e "${RED}❌ Docker is not installed${NC}"
    echo -e "${YELLOW}Please install Docker first:${NC}"
    echo "   Ubuntu/Debian: sudo apt-get update && sudo apt-get install docker.io"
    echo "   CentOS/RHEL:   sudo yum install docker"
    echo "   Or visit: https://docs.docker.com/engine/install/"
    exit 1
fi

if ! docker info >/dev/null 2>&1; then
    echo -e "${RED}❌ Docker is not running${NC}"
    echo -e "${YELLOW}Please start Docker:${NC}"
    echo "   sudo systemctl start docker"
    echo "   sudo service docker start"
    exit 1
fi

echo -e "${GREEN}✅ Docker is installed and running${NC}"

# Check Docker version
DOCKER_VERSION=$(docker --version | cut -d' ' -f3 | cut -d',' -f1)
echo -e "${BLUE}   Docker version: ${DOCKER_VERSION}${NC}"

# Check if we're in the right directory
if [[ ! -f "Dockerfile" ]]; then
    echo -e "${RED}❌ Dockerfile not found in current directory${NC}"
    echo -e "${YELLOW}Please run this script from the mqtt-sql-logger directory${NC}"
    exit 1
fi

if [[ ! -f "MqttSqlLogger/MqttSqlLogger.csproj" ]]; then
    echo -e "${RED}❌ MqttSqlLogger project not found${NC}"
    echo -e "${YELLOW}Please ensure you're in the correct directory${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Project files found${NC}"
echo ""

# Display build information
echo -e "${BLUE}📋 Build Information:${NC}"
echo -e "   Image name: ${FULL_IMAGE_NAME}"
echo -e "   Architecture: $(uname -m)"
echo -e "   Build context: $(pwd)"
echo -e "   Dockerfile: $(pwd)/Dockerfile"
echo ""

# Ask for confirmation
read -p "Continue with build? (y/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo -e "${YELLOW}Build cancelled${NC}"
    exit 0
fi

# Clean up old images (optional)
echo -e "${BLUE}🧹 Cleaning up old images...${NC}"
OLD_IMAGES=$(docker images "${IMAGE_NAME}" --format "table {{.Repository}}:{{.Tag}}\t{{.ID}}" | grep -v "REPOSITORY" || true)
if [[ -n "$OLD_IMAGES" ]]; then
    echo -e "${YELLOW}Found existing images:${NC}"
    echo "$OLD_IMAGES"
    echo ""
fi

# Build the Docker image
echo -e "${BLUE}🔨 Building Docker image...${NC}"
echo -e "${BLUE}Command: docker build -t ${FULL_IMAGE_NAME} .${NC}"
echo ""

BUILD_START=$(date +%s)

# Build with progress output
if docker build -t "${FULL_IMAGE_NAME}" .; then
    BUILD_END=$(date +%s)
    BUILD_TIME=$((BUILD_END - BUILD_START))
    
    echo ""
    echo -e "${GREEN}✅ Build completed successfully!${NC}"
    echo -e "${GREEN}   Build time: ${BUILD_TIME} seconds${NC}"
else
    echo ""
    echo -e "${RED}❌ Build failed!${NC}"
    echo -e "${YELLOW}Check the error messages above for details${NC}"
    exit 1
fi

# Display image information
echo ""
echo -e "${BLUE}📊 Image Information:${NC}"
docker images "${FULL_IMAGE_NAME}" --format "table {{.Repository}}\t{{.Tag}}\t{{.ID}}\t{{.Size}}\t{{.CreatedSince}}"

# Get image size
IMAGE_SIZE=$(docker images "${FULL_IMAGE_NAME}" --format "{{.Size}}")
echo -e "${BLUE}   Final image size: ${IMAGE_SIZE}${NC}"

# Test the image
echo ""
echo -e "${BLUE}🧪 Testing the image...${NC}"
if docker run --rm "${FULL_IMAGE_NAME}" --help >/dev/null 2>&1; then
    echo -e "${GREEN}✅ Image test passed${NC}"
else
    echo -e "${YELLOW}⚠️  Image test failed (this might be expected if no --help option)${NC}"
fi

# Security scan (if available)
if command -v docker-bench-security &> /dev/null; then
    echo ""
    echo -e "${BLUE}🔒 Running security scan...${NC}"
    docker-bench-security
fi

echo ""
echo -e "${GREEN}🎉 Build completed successfully!${NC}"
echo ""
echo -e "${BLUE}📋 Next Steps:${NC}"
echo -e "${BLUE}=============${NC}"
echo ""
echo -e "${YELLOW}1. Test the image locally:${NC}"
echo "   docker run --rm -e MQTT__BROKER_HOST=test.mosquitto.org \\"
echo "     -e SQL__CONNECTION_STRING='your-connection-string' \\"
echo "     ${FULL_IMAGE_NAME}"
echo ""
echo -e "${YELLOW}2. Run in background:${NC}"
echo "   docker run -d --name mqtt-sql-logger --restart unless-stopped \\"
echo "     -e MQTT__BROKER_HOST='your-broker' \\"
echo "     -e SQL__CONNECTION_STRING='your-db-connection' \\"
echo "     ${FULL_IMAGE_NAME}"
echo ""
echo -e "${YELLOW}3. Use with Portainer:${NC}"
echo "   - Container name: mqtt-sql-logger"
echo "   - Image: ${FULL_IMAGE_NAME}"
echo "   - Add environment variables for your MQTT and SQL settings"
echo ""
echo -e "${YELLOW}4. Push to registry (optional):${NC}"
echo "   docker tag ${FULL_IMAGE_NAME} yourusername/mqtt-sql-logger:${IMAGE_TAG}"
echo "   docker push yourusername/mqtt-sql-logger:${IMAGE_TAG}"
echo ""
echo -e "${YELLOW}5. View logs:${NC}"
echo "   docker logs mqtt-sql-logger -f"
echo ""
echo -e "${GREEN}Happy logging! 📊${NC}"