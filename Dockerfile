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

ENV CORECLR_ENABLE_PROFILING=1 \
CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A} \
CORECLR_NEWRELIC_HOME=/app/newrelic \
CORECLR_PROFILER_PATH=/app/newrelic/linux-arm64/libNewRelicProfiler.so \
NEW_RELIC_APP_NAME="O11yParty-Buzzer" \
NEW_RELIC_LOG_LEVEL=debug

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "O11yPartyBuzzer.dll"]
