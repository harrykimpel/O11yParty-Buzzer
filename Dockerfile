FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY O11yPartyBuzzer.csproj ./
RUN dotnet restore O11yPartyBuzzer.csproj

COPY . ./
RUN dotnet publish O11yPartyBuzzer.csproj -c Release -o /app/publish

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

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "O11yPartyBuzzer.dll"]
