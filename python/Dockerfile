# ── Stage 1 : Build Blazor WASM ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS blazor-build
WORKDIR /src
COPY src/CoolSleep.Web/ ./CoolSleep.Web/
RUN dotnet publish CoolSleep.Web/CoolSleep.Web.csproj \
    -c Release -o /out/web

# ── Stage 2 : Build ASP.NET Core API ─────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY src/CoolSleep.Api/ ./CoolSleep.Api/
RUN dotnet publish CoolSleep.Api/CoolSleep.Api.csproj \
    -c Release -o /out/api

# ── Stage 3 : Runtime final ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y \
    python3 python3-pip nginx supervisor \
    && rm -rf /var/lib/apt/lists/*

# FastAPI
COPY python/thermal/requirements.txt /tmp/
RUN pip3 install -r /tmp/requirements.txt --break-system-packages
COPY python/thermal/ /app/thermal/

# ASP.NET Core API
COPY --from=api-build /out/api /app/api/

# Blazor WASM → Nginx
COPY --from=blazor-build /out/web/wwwroot /var/www/coolsleep/

# Configs
COPY deploy/nginx.conf       /etc/nginx/nginx.conf
COPY deploy/supervisord.conf /etc/supervisor/conf.d/coolsleep.conf

EXPOSE 80
CMD ["/usr/bin/supervisord", "-n"]
