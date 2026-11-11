FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG PROJECT
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src ./src
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app --property:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0
ARG PROJECT
ENV PROJECT=${PROJECT} DOTNET_EnableDiagnostics=0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "dotnet ${PROJECT}.dll"]
