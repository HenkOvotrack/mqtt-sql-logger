# Use the official .NET 8 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /app

# Copy csproj and restore dependencies
COPY MqttSqlLogger/*.csproj ./MqttSqlLogger/
COPY *.sln ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish MqttSqlLogger/MqttSqlLogger.csproj -c Release -o out --no-restore

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

# Create a non-root user for security
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Copy the published application
COPY --from=build-env /app/out .

# Set environment variables with defaults (can be overridden at runtime)
ENV MQTT__BROKER_HOST=localhost
ENV MQTT__BROKER_PORT=1883
ENV MQTT__CLIENT_ID=mqtt-sql-logger-docker
ENV MQTT__TOPICS=#
ENV MQTT__QOS=1
ENV SQL__CONNECTION_STRING="Server=localhost;Database=MqttLogs;Trusted_Connection=True;TrustServerCertificate=True;"
ENV SQL__CREATE_TABLE=true
ENV LOG__LEVEL=Information

# Health check to ensure the application is running
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD ps aux | grep -v grep | grep -q MqttSqlLogger || exit 1

# Expose no ports (this is a client application, not a server)
# The application connects outbound to MQTT broker and SQL Server

ENTRYPOINT ["dotnet", "MqttSqlLogger.dll"]