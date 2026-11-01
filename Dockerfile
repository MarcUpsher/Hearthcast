# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY backend/src/Hearthcast.Data/Hearthcast.Data.csproj backend/src/Hearthcast.Data/
COPY backend/src/Hearthcast.Api/Hearthcast.Api.csproj backend/src/Hearthcast.Api/
RUN dotnet restore backend/src/Hearthcast.Api/Hearthcast.Api.csproj
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/Hearthcast.Api/Hearthcast.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM build AS test
COPY backend/tools/ backend/tools/
COPY backend/tests/ backend/tests/
RUN dotnet restore backend/tests/Hearthcast.Data.Tests/Hearthcast.Data.Tests.csproj
RUN dotnet build backend/tests/Hearthcast.Data.Tests/Hearthcast.Data.Tests.csproj \
    --configuration Release --no-restore
RUN dotnet restore backend/tests/Hearthcast.Api.Tests/Hearthcast.Api.Tests.csproj \
    && dotnet build backend/tests/Hearthcast.Api.Tests/Hearthcast.Api.Tests.csproj --configuration Release --no-restore
COPY Hearthcast.slnx ./
ENTRYPOINT ["dotnet", "test", "Hearthcast.slnx", "--configuration", "Release", "--no-build", "--no-restore"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Hearthcast.Api.dll"]
