#!/bin/bash

# Template for testing environment variables
# Copy this file to test-env.sh and replace with your actual values

echo "Testing environment variables:"
echo "MQTT__BROKER_HOST=$MQTT__BROKER_HOST"
echo "MQTT__BROKER_PORT=$MQTT__BROKER_PORT" 
echo "MQTT__CLIENT_ID=$MQTT__CLIENT_ID"
echo "SQL__CONNECTION_STRING=$SQL__CONNECTION_STRING"

# Run the app with debug logging
cd /Users/henkbeekhuis/source/test/mqttlogger
export MQTT__BROKER_HOST=your-mqtt-broker-host
export MQTT__BROKER_PORT=1883
export MQTT__CLIENT_ID=mqtt-test-debug
export MQTT__USERNAME=your-mqtt-username
export MQTT__PASSWORD=your-mqtt-password
export MQTT__TOPICS="your-topic1/#,your-topic2/#"
export MQTT__QOS=1
export SQL__CONNECTION_STRING="Server=your-sql-server;Database=your-database;User Id=your-username;Password=your-password;TrustServerCertificate=True;"
export SQL__CREATE_TABLE=true
export LOG__LEVEL=Debug

echo "After export:"
echo "MQTT__BROKER_HOST=$MQTT__BROKER_HOST"
echo "MQTT__BROKER_PORT=$MQTT__BROKER_PORT"

dotnet run --project MqttSqlLogger/MqttSqlLogger.csproj