FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY O11yPartyBuzzer.csproj ./
RUN dotnet restore O11yPartyBuzzer.csproj

COPY . ./
RUN dotnet publish O11yPartyBuzzer.csproj -c Release -o /app/publish
COPY newrelic.config /app/publish/newrelic/newrelic.config

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Listen on a single container port by default.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# New Relic settings can be provided via environment variables at runtime.
# Example: NewRelic__AccountId=1234567
# Example: NewRelic__UserApiKey=your-user-api-key
ENV NewRelic__AccountId=
ENV NewRelic__IngestApiKey=

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
