FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MqttSqlLogger/MqttSqlLogger.csproj", "MqttSqlLogger/"]
RUN dotnet restore "MqttSqlLogger/MqttSqlLogger.csproj"
COPY . .
WORKDIR "/src/MqttSqlLogger"
RUN dotnet build "MqttSqlLogger.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MqttSqlLogger.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables with defaults (can be overridden at runtime)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "MqttSqlLogger.dll"]
