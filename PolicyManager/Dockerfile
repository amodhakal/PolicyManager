FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["PolicyManager/PolicyManager.csproj", "PolicyManager/"]
RUN dotnet restore "PolicyManager/PolicyManager.csproj"
COPY PolicyManager/ PolicyManager/
WORKDIR "/src/PolicyManager"
RUN dotnet build "./PolicyManager.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./PolicyManager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
RUN dotnet tool install --tool-path /app/tools dotnet-ef --version 9.0.14
RUN /app/tools/dotnet-ef migrations bundle --project "/src/PolicyManager/PolicyManager.csproj" --configuration $BUILD_CONFIGURATION -o /app/migrate --force

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=publish /app/migrate ./migrate
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET / HTTP/1.0\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q 'HTTP/1.1'"]
ENTRYPOINT ["/bin/sh", "-c", "if [ -z \"${DB_HOST:-}\" ]; then echo \"DB_HOST is not set, skipping database migrations.\" >&2; else n=0; until /app/migrate --connection \"Server=${DB_HOST};Database=${DB_NAME:-PolicyManager};User Id=${DB_USER:-SA};Password=${DB_PASSWORD};TrustServerCertificate=True\"; do n=$((n + 1)); if [ \"$n\" -ge 30 ]; then echo \"Database migrations failed after $n attempts, exiting.\" >&2; exit 1; fi; echo \"Migration attempt $n failed, retrying in 2s.\" >&2; sleep 2; done; fi; exec dotnet PolicyManager.dll"]
