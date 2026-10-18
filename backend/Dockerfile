# syntax=docker/dockerfile:1

# ---- build -----------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src

# Restore first, from project files and lock files only, so the layer is cached until a
# dependency changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/StockData.Api/StockData.Api.csproj src/StockData.Api/packages.lock.json src/StockData.Api/
COPY src/StockData.Application/StockData.Application.csproj src/StockData.Application/packages.lock.json src/StockData.Application/
COPY src/StockData.Infrastructure/StockData.Infrastructure.csproj src/StockData.Infrastructure/packages.lock.json src/StockData.Infrastructure/
RUN dotnet restore src/StockData.Api/StockData.Api.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/StockData.Api/StockData.Api.csproj -c Release --no-restore -o /app /p:UseAppHost=false

# ---- runtime ---------------------------------------------------------------------------------
# The full (not chiseled/alpine) ASP.NET image: it ships ICU and tzdata, which exchange-timezone
# grouping needs (e.g. Asia/Tokyo). Without them the API falls back to a fixed UTC offset.
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true
EXPOSE 8080
USER $APP_UID

# No curl/wget in the image; bash's /dev/tcp is enough for a health probe.
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && grep -q Healthy <&3"]

ENTRYPOINT ["dotnet", "StockData.Api.dll"]
