FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src ./src
RUN dotnet publish src/Telemetry.Api/Telemetry.Api.csproj -c Release -o /app --property:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0
RUN apt-get update \
    && apt-get install --no-install-recommends --yes wget \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Telemetry.Api.dll"]
