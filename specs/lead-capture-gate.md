# Spec: Lead Capture Gate

Owner: [Components/Pages/Home.razor](../Components/Pages/Home.razor)
Contract: [Services/INewRelicEventPublisher.cs](../Services/INewRelicEventPublisher.cs) → `PublishLeadCaptureAsync`

## Invariants

- Buzzer is unreachable until GATE-02 completes successfully
- A successful submission produces exactly one `O11yPartyLeadCapture` event — no retry on success
- Gate state persists for the duration of the Blazor circuit; refreshing resets it

## Scenarios

### GATE-01: Attendee sees gate on first load

```
Given: a fresh browser session
When:  attendee navigates to /
Then:  lead capture modal is visible
  And: buzz button is not rendered
```

### GATE-02: Attendee submits valid lead

```
Given: all six fields filled with valid values
  And: email matches ^[^\s@]+@[^\s@]+\.[^\s@]{2,}$
When:  attendee clicks Submit
Then:  PublishLeadCaptureAsync is called once with firstName, lastName,
       businessEmailAddress, companyName, jobTitle, country
  And: modal is dismissed
  And: buzzer becomes active
```

### GATE-03: Attendee submits with invalid email

```
Given: email field contains a value that does not match the regex
       (e.g. "notanemail", "foo@", "@bar.com")
When:  attendee clicks Submit
Then:  PublishLeadCaptureAsync is NOT called
  And: modal remains visible
  And: an error is shown on the email field
```

### GATE-04: Attendee submits with a required field empty

```
Given: one or more of the six required fields is blank
When:  attendee clicks Submit
Then:  PublishLeadCaptureAsync is NOT called
  And: modal remains visible
  And: an error is shown on the blank field(s)
```

### GATE-05: New Relic ingest returns non-2xx on lead submit

```
Given: all six fields are valid
  And: PublishLeadCaptureAsync returns an error or throws
When:  attendee clicks Submit
Then:  modal remains visible
  And: attendee sees an error message
  And: buzzer does NOT become active
```

### GATE-06: Attendee navigates back after completing gate

```
Given: gate was previously completed successfully in this Blazor circuit
When:  attendee returns to /
Then:  modal is NOT shown
  And: buzzer is immediately active
```
