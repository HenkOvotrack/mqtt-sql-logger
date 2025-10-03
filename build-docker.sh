#!/bin/bash

# MQTT SQL Logger - Docker Build Script
# This script builds the Docker image for the MQTT SQL Logger application

set -e  # Exit on any error

echo "🚀 Building MQTT SQL Logger Docker Image..."

# Check if Docker is running
if ! docker info >/dev/null 2>&1; then
    echo "❌ Error: Docker is not running. Please start Docker Desktop and try again."
    exit 1
fi

# Build the Docker image
echo "📦 Building image..."
docker build -t mqtt-sql-logger:latest .

# Check if build was successful
if [ $? -eq 0 ]; then
    echo "✅ Build successful!"
    echo ""
    echo "📋 Image details:"
    docker images mqtt-sql-logger:latest
    echo ""
    echo "🎯 Next steps:"
    echo "1. Test the image locally:"
    echo "   docker run --rm mqtt-sql-logger:latest --help"
    echo ""
    echo "2. Run with environment variables:"
    echo "   docker run -d --name mqtt-sql-logger \\"
    echo "     -e MQTT__BROKER_HOST='your-broker' \\"
    echo "     -e SQL__CONNECTION_STRING='your-connection-string' \\"
    echo "     mqtt-sql-logger:latest"
    echo ""
    echo "3. Use with Portainer:"
    echo "   - Image name: mqtt-sql-logger:latest"
    echo "   - See README-Docker.md for detailed instructions"
    echo ""
    echo "4. Push to registry (optional):"
    echo "   docker tag mqtt-sql-logger:latest yourusername/mqtt-sql-logger:latest"
    echo "   docker push yourusername/mqtt-sql-logger:latest"
else
    echo "❌ Build failed!"
    exit 1
fi