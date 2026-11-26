# Marketing platform API (the stub's contract)

The lead form sends leads to a marketing automation platform (the "MAP", threat model 0001). No real platform has been chosen yet (decision D4), so the site is built against this small, generic API. Locally and in tests a WireMock.Net stub serves it. Choosing a real platform means mapping this contract onto its API, adding contract tests against that API, and reopening the threat model.

The client is `MapClient` in `src/UmbracoSample.Leads`; its contract tests are in `tests/UmbracoSample.Leads.Tests`.

## Requests

Both requests are `POST` with a JSON body, relative to the configured `Leads:Map:BaseAddress`. Both carry the credential in an `X-Api-Key` header. The idempotency key travels only in a header or a body, never in a URL, so HTTP client spans and access logs cannot record it.

### Create a lead: `POST leads`

| Header | Value |
|---|---|
| `Idempotency-Key` | The submission's key, from the signed render token. The MAP keeps one lead per key. |
| `X-Api-Key` | The credential, scoped to creating and looking up leads |

```json
{
  "name": "Ada Lovelace",
  "email": "ada@example.com",
  "company": "Analytical Engines",
  "message": "Hello",
  "marketingConsent": true,
  "consentVersion": "2026-11",
  "submittedAt": "2026-11-26T09:00:00+00:00",
  "listId": "list-1"
}
```

| Response | Meaning | Client outcome |
|---|---|---|
| `201 Created` | New lead | Accepted |
| `200 OK` | The key was already used: the existing lead stands | Accepted (the visitor sees the same as a new lead) |
| `400` or `422` | The MAP refused the lead | Rejected, no retry |
| anything else, or no connection | The MAP may or may not have it | One retry with the same key |

### Look up a key: `POST leads/lookup`

```json
{ "idempotencyKey": "..." }
```

| Response | Meaning |
|---|---|
| `200 OK` | A lead with this key exists |
| `404 Not Found` | No lead has this key |
| anything else | Cannot tell |

## How the client uses it

- **Budget.** One submission gets 10 s in all (`Leads:Map:Budget`), and each call at most 5 s (`Leads:Map:CreateTimeout`).
- **Retries.** At most one retry, after a random pause of up to 500 ms, always with the same key. ServiceDefaults' standard resilience handler is removed from this client, so retries do not multiply.
- **Timeouts.** A timed-out create is followed by a lookup:
  - found: accepted;
  - not found: one more create;
  - cannot tell: the outcome is unknown, and the visitor is asked not to resubmit for a few minutes.
- **Disconnects.** A visitor who disconnects cancels the call, and the outcome is unknown. Their next submission reuses the key and is reconciled.
- **Bulkhead.** At most 20 calls are in flight per replica (`Leads:Map:MaxConcurrentCalls`). Beyond that, a submission is refused at once.
- **No trace context.** Requests carry no `traceparent` or `tracestate`.
- **Logging.** The client logs only the outcome and the kind of the last attempt: never field values, the key, the credential or a response body.

## Fidelity limits of the stub

The WireMock stub answers with fixed responses per test. It does not:

- store leads;
- de-duplicate by key;
- enforce the credential;
- apply a quota.

The tests script those behaviours instead: duplicate as `200`, lookup as `200` or `404`, outages and delays. Any real MAP must meet the D4 conditions in threat model 0001: idempotency keys (or an external ID with de-duplication), lookup by key, and a quota above the outbound cap of 20 per replica.
