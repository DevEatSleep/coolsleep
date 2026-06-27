# ── Stage 1 : Build Blazor WASM ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS blazor-build
WORKDIR /src
COPY CoolSleep.Web/           ./CoolSleep.Web/
COPY CoolSleep.Shared/        ./CoolSleep.Shared/
RUN dotnet publish CoolSleep.Web/CoolSleep.Web.csproj \
    -c Release -o /out/web

# ── Stage 2 : Build ASP.NET Core API ─────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY . .
RUN dotnet publish CoolSleep.Api/CoolSleep.Api.csproj \
    -c Release -o /out/api

# ── Stage 3 : Runtime final ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y \
    python3 python3-pip nginx supervisor \
    && rm -rf /var/lib/apt/lists/*

# FastAPI + dépendances Python
COPY thermal_service/requirements.txt /tmp/
RUN pip3 install -r /tmp/requirements.txt --break-system-packages

COPY thermal_service/ /app/thermal/

# ASP.NET Core API
COPY --from=api-build /out/api /app/api/

# Blazor WASM → servi statiquement par Nginx
COPY --from=blazor-build /out/web/wwwroot /var/www/coolsleep/

# Configs runtime
COPY deploy/nginx.conf        /etc/nginx/nginx.conf
COPY deploy/supervisord.conf  /etc/supervisor/conf.d/coolsleep.conf

EXPOSE 80
CMD ["/usr/bin/supervisord", "-n"]
