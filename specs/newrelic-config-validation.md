# Spec: New Relic Configuration Validation

Owner: [Services/NewRelicEventPublisher.cs](../Services/NewRelicEventPublisher.cs)
Config: [Services/NewRelicOptions.cs](../Services/NewRelicOptions.cs)

## Invariants

- Region is resolved at startup; the ingest endpoint does not change at runtime
- A blank `IngestApiKey` or `AccountId` must never silently produce no-op publishes

## Scenarios

### CFG-01: IngestApiKey is blank at startup

```
Given: appsettings NewRelic:IngestApiKey = ""
When:  application starts (or first publish is attempted)
Then:  a descriptive configuration error is thrown
  And: no HTTP request is sent to New Relic
```

### CFG-02: AccountId is blank at startup

```
Given: appsettings NewRelic:AccountId = ""
When:  application starts (or first publish is attempted)
Then:  a descriptive configuration error is thrown
  And: no HTTP request is sent to New Relic
```

### CFG-03: Region = "EU" selects the EU ingest endpoint

```
Given: appsettings NewRelic:Region = "EU"
When:  PublishBuzzAsync or PublishLeadCaptureAsync is called
Then:  the HTTP request targets insights-collector.eu01.nr-data.net
```

### CFG-04: Region = "US" selects the US ingest endpoint

```
Given: appsettings NewRelic:Region = "US"
When:  PublishBuzzAsync or PublishLeadCaptureAsync is called
Then:  the HTTP request targets insights-collector.newrelic.com
```

### CFG-05: Region omitted defaults to US

```
Given: appsettings NewRelic:Region is not set
When:  PublishBuzzAsync or PublishLeadCaptureAsync is called
Then:  the HTTP request targets insights-collector.newrelic.com
```

### CFG-06: Custom EventType is reflected in buzz payload

```
Given: appsettings NewRelic:EventType = "MyCustomBuzz"
When:  PublishBuzzAsync is called
Then:  the posted JSON contains eventType = "MyCustomBuzz"
```

### CFG-07: Custom LeadCaptureEventType is reflected in lead payload

```
Given: appsettings NewRelic:LeadCaptureEventType = "MyCustomLead"
When:  PublishLeadCaptureAsync is called
Then:  the posted JSON contains eventType = "MyCustomLead"
```
