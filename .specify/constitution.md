# Specification Constitution — O11yParty-Buzzer

## Purpose

This constitution defines the shared vocabulary, system boundaries, and invariants for specifying behavior in O11yParty-Buzzer. It is the source of truth for what the system must do, must not do, and assumes about its environment.

## 1. Actors

| Actor | Description |
| --- | --- |
| Attendee | A human at the event who interacts with the UI |
| New Relic Ingest | The external API that receives custom events |
| App Runner | The reverse proxy that sits in front of the app |

## 2. Core Concepts

**Lead Gate** — A form the attendee must complete before gaining access to the buzzer. Completion is permanent for the duration of the browser session.

**Buzz** — A single intentional action by an attendee that produces exactly one `O11yPartyBuzz` event in New Relic.

**Chaos Mode** — A query-string-activated synthetic failure state used for demo purposes. Not a production concern; specs should treat it as a named variant, not a bug.

**Event Type** — The New Relic custom event schema name. Configurable; defaults are `O11yPartyBuzz` and `O11yPartyLeadCapture`.

## 3. Invariants (always true, regardless of path)

- The buzzer is unreachable until the lead gate has been submitted successfully.
- A buzz requires a non-empty team name (max 80 chars).
- Every successful buzz produces exactly one event posted to New Relic.
- Lead capture produces exactly one event posted to New Relic.
- The region (US / EU) is resolved at startup from config; the endpoint does not change at runtime.
- The app is stateless — no session, no database, no caching of leads or buzzes.

## 4. Out-of-Scope (do not specify)

- New Relic's behavior after receiving an event (that's their SLA)
- Theme (dark/light) persistence — cosmetic, not behavioral
- The Counter and Weather pages — scaffold remnants, not features
- Docker / App Runner infrastructure behavior

## 5. Spec Patterns

Format: **Given / When / Then** at the scenario level.

Scenario naming: Use the actor's perspective — "Attendee buzzes before completing lead gate", not "LeadGateValidation_returns_false".

**Required attributes for buzz specs:**

- `teamName` value (or shape: empty, max-length, typical)
- Whether lead gate was already completed
- Expected New Relic payload (or: no call made)

**Required attributes for lead capture specs:**

- All six fields (first, last, email, company, title, country)
- Email validity (valid / invalid format)
- Expected New Relic payload (or: no call made)

Chaos specs should specify the `chaos` query param value as a first-class input, not treat it as implicit context.

## 6. Edge Cases That Must Be Specified

| Scenario | Why it matters |
| --- | --- |
| `PublishBuzzAsync` returns non-2xx | UI must show an error state, not silently fail |
| `IngestApiKey` or `AccountId` is blank at startup | Should fail fast, not produce silent no-ops |
| Team name is whitespace-only | Should be treated as empty (invariant above) |
| Attendee submits lead gate twice (back button) | Must not produce duplicate lead events |
| `chaos=timeout` (35s) during buzz | UI must not hang indefinitely |

## 7. Spec Ownership

- `Services/INewRelicEventPublisher.cs` — contract owner for all event publishing specs
- `Components/Pages/Home.razor` — owner for gate, buzz, and chaos flow specs
- `Services/NewRelicOptions.cs` — owner for configuration validation specs
