# Spec: Buzz Flow

Owner: [Components/Pages/Home.razor](../Components/Pages/Home.razor)
Contract: [Services/INewRelicEventPublisher.cs](../Services/INewRelicEventPublisher.cs) → `PublishBuzzAsync`

## Invariants

- Lead gate must be completed first (see GATE-02 in lead-capture-gate.md)
- Team name must be non-empty after trim; max 80 chars
- Each button press produces at most one New Relic event

## Scenarios

### BUZZ-01: Attendee buzzes with a valid team name

```
Given: lead gate completed
  And: teamName = "Team Rocket" (non-empty, ≤80 chars)
When:  attendee clicks BUZZ
Then:  PublishBuzzAsync is called once with teamName="Team Rocket"
  And: success status message is shown
```

### BUZZ-02: Attendee buzzes with an empty team name

```
Given: lead gate completed
  And: teamName = "" (or whitespace-only)
When:  attendee clicks BUZZ
Then:  PublishBuzzAsync is NOT called
  And: a validation error is shown
```

### BUZZ-03: Attendee buzzes with a max-length team name

```
Given: lead gate completed
  And: teamName is exactly 80 characters
When:  attendee clicks BUZZ
Then:  PublishBuzzAsync is called once with the full 80-char string
  And: success status message is shown
```

### BUZZ-04: New Relic ingest returns non-2xx on buzz

```
Given: lead gate completed
  And: teamName is valid
  And: PublishBuzzAsync throws or returns an error
When:  attendee clicks BUZZ
Then:  error status message is shown
  And: buzzer remains active (attendee can retry)
```

### BUZZ-05: Attendee buzzes multiple times in sequence

```
Given: lead gate completed
  And: each buzz has a valid team name
When:  attendee clicks BUZZ three times (waiting for each to resolve)
Then:  PublishBuzzAsync is called exactly three times
  And: each call is independent
```
