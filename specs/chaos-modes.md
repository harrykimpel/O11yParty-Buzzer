# Spec: Chaos Modes

Owner: [Components/Pages/Home.razor](../Components/Pages/Home.razor)

## Invariants

- Chaos modes are activated exclusively via the `chaos` query parameter
- Chaos modes are demo/testing features — they must not affect behavior when the parameter is absent
- All chaos modes should add a custom attribute to the New Relic transaction for traceability
- Chaos modes are **blocked in Production**: any `?chaos=` parameter in a Production environment is silently ignored and a warning is logged

## Scenarios

### CHAOS-01: No chaos parameter — normal behavior

```plain
Given: URL does not include ?chaos=
When:  attendee clicks BUZZ
Then:  buzz proceeds normally with no artificial delay, failure, or timeout
```

### CHAOS-02: chaos=latency delays the buzz

```plain
Given: URL includes ?chaos=latency
  And: optional &latencyMs=5000 (default: 3000ms)
When:  attendee clicks BUZZ
Then:  buzz is delayed by latencyMs before the event is sent
  And: after the delay, PublishBuzzAsync IS called normally
  And: success or error status is shown as usual
```

### CHAOS-03: chaos=exception throws on every buzz

```plain
Given: URL includes ?chaos=exception
When:  attendee clicks BUZZ
Then:  an unhandled exception is raised before PublishBuzzAsync is called
  And: New Relic captures the exception as an error event
```

### CHAOS-04: chaos=random fails approximately half of buzzes

```plain
Given: URL includes ?chaos=random
When:  attendee clicks BUZZ
Then:  ~50% of clicks produce an error state (PublishBuzzAsync not called or fails)
  And: ~50% of clicks succeed normally
  And: the outcome is not deterministic per click
```

### CHAOS-05: chaos=timeout simulates a hung request

```plain
Given: URL includes ?chaos=timeout
When:  attendee clicks BUZZ
Then:  the operation hangs for ~35 seconds
  And: UI does not block indefinitely — a timeout or error state is shown
  And: attendee can recover (buzzer remains usable after the timeout resolves)
```

### CHAOS-06: Unknown chaos value is treated as no-op

```plain
Given: URL includes ?chaos=unknown_value
When:  attendee clicks BUZZ
Then:  buzz proceeds normally as if no chaos parameter was present
```
