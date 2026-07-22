# Run the SDK natively on the build host's arch ($BUILDPLATFORM) so MSBuild is never emulated
# under QEMU — cross-arch emulation crashes MSBuild property-function evaluation (e.g.
# "[MSBuild]::GetTargetFrameworkVersion(net6.0) cannot be evaluated" during restore). This is a
# framework-dependent app launched via `dotnet O11yPartyBuzzer.dll`, so the published output is
# portable IL and runs fine on the amd64 runtime image below regardless of build host arch.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY O11yPartyBuzzer.csproj ./
RUN dotnet restore O11yPartyBuzzer.csproj

COPY . ./
RUN dotnet publish O11yPartyBuzzer.csproj -c Release -o /app/publish
COPY newrelic.config /app/publish/newrelic/newrelic.config

# Pinned to amd64 regardless of build host — standard ECS Fargate/App Runner run x86_64, so an
# unpinned FROM here would silently produce an arm64 image on an Apple Silicon build host and
# fail at container startup with "exec format error".
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Listen on a single container port by default.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# New Relic settings can be provided via environment variables at runtime.
# Example: NewRelic__AccountId=1234567
# Example: NewRelic__UserApiKey=your-user-api-key
ENV NewRelic__AccountId=
ENV NewRelic__IngestApiKey=

# Buzz hub (SignalR) — game hub URL + shared secret (must match the game). Inject at runtime.
# BuzzHub__Url example: https://<game-host>/hubs/buzz
ENV BuzzHub__Url=
ENV BuzzHub__SharedSecret=

# Enable the agent
ENV NEW_RELIC_LICENSE_KEY=

# CORECLR_PROFILER_PATH is set at container startup by docker-entrypoint.sh
# based on the detected CPU architecture (x86_64 → linux-x64, aarch64 → linux-arm64),
# so the same image runs on both arm64 and amd64 hosts.
ENV CORECLR_ENABLE_PROFILING=1 \
CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A} \
CORECLR_NEWRELIC_HOME=/app/newrelic \
NEW_RELIC_APP_NAME="O11yParty-Buzzer" \
NEW_RELIC_LOG_LEVEL=info

EXPOSE 8080

COPY --from=build /app/publish .
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
