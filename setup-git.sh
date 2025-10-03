#!/bin/bash

# Git Repository Setup Script for MQTT SQL Logger
# Run this script to initialize the git repository and prepare for first commit

set -e

echo "🚀 Initializing Git Repository for MQTT SQL Logger"
echo "=================================================="

# Check if we're already in a git repository
if [ -d .git ]; then
    echo "❓ Git repository already exists. Do you want to reinitialize? (y/N)"
    read -r response
    if [[ "$response" =~ ^([yY][eE][sS]|[yY])$ ]]; then
        rm -rf .git
        echo "🗑️  Removed existing git repository"
    else
        echo "✅ Keeping existing git repository"
        exit 0
    fi
fi

# Initialize git repository
echo "📁 Initializing git repository..."
git init

# Set default branch name (modern practice)
git branch -M main

# Add all files
echo "📄 Adding files to git..."
git add .

# Check git status
echo "📊 Git status:"
git status

# Create initial commit
echo "💾 Creating initial commit..."
git commit -m "Initial commit: MQTT SQL Logger with Docker support

Features:
- .NET 8 MQTT to SQL Server logger
- Docker containerization with multi-stage build
- Portainer deployment support
- Comprehensive documentation
- Linux deployment automation
- Security best practices
- Health monitoring and reconnection logic"

echo ""
echo "✅ Git repository initialized successfully!"
echo ""
echo "📋 Next Steps:"
echo "=============="
echo ""
echo "1. Create a repository on GitHub/GitLab/Bitbucket"
echo ""
echo "2. Add the remote origin:"
echo "   git remote add origin https://github.com/yourusername/mqtt-sql-logger.git"
echo ""
echo "3. Push to remote repository:"
echo "   git push -u origin main"
echo ""
echo "4. Clone on your Linux server:"
echo "   git clone https://github.com/yourusername/mqtt-sql-logger.git"
echo ""
echo "5. Build and deploy on Linux:"
echo "   cd mqtt-sql-logger"
echo "   ./build-linux.sh"
echo ""
echo "📖 Documentation files created:"
echo "   - README.md              (Main project documentation)"
echo "   - README-Docker.md       (Docker deployment guide)"
echo "   - DEPLOY-Linux.md        (Linux-specific deployment)"
echo ""
echo "🐳 Docker files created:"
echo "   - Dockerfile             (Multi-stage Docker build)"
echo "   - .dockerignore          (Build optimization)"
echo "   - docker-compose.yml     (Complete stack example)"
echo "   - build-linux.sh         (Linux build automation)"
echo ""
echo "🔧 Configuration files:"
echo "   - .gitignore             (Git ignore rules)"
echo "   - portainer-template.json (Portainer deployment template)"
echo ""
echo "Happy coding! 🎉"