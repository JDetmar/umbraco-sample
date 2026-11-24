# Threat model 0001: lead form forwarded to the marketing automation platform

- Status: **Proposed. It needs owner approval before any form code is written (ES-023).**
- Date: 2026-11-24
- Scope: plan Phase 4, the Form component and its submission endpoint
- Clauses: ES-022, ES-023, ES-025, ES-026, ES-043, ES-044, ES-045, ES-046, ES-058, ES-059; framework TST-07

## 1. What is being built

Editors place a **Form** block on a page. A visitor fills in a short lead form and submits it. The site validates the submission and forwards it to the marketing automation platform (the "MAP"). The site then shows a thank-you message, but only after the MAP has accepted the lead.

- **Nothing is stored by the site.** There is no database table, queue or file for submissions. The MAP is the system of record, which keeps ES-058 and ES-059 small.
- **The MAP is a stub.** Locally and in CI it is a WireMock.Net stub (approved, ES-006), with its fidelity limits documented. A real MAP is a configuration change plus contract tests against its documented API.
- **The field set is fixed in code**, not configurable by editors:
  - name;
  - work email;
  - company (optional);
  - message (optional);
  - a marketing consent checkbox (unchecked by default);
  - the consent text version.
  Editors choose only the heading, the intro text and the MAP list the lead goes to, from an allowlisted set.

## 2. Data flow and trust boundaries

```
Visitor browser ──(1)──► Edge (Front Door + WAF) ──(2)──► Site container ──(3)──► MAP API
                                                            │
Editor (back office) ──(4)── configures the Form block ─────┘
```

| # | Boundary | Who is on the other side | Trust |
|---|---|---|---|
| 1 | Internet to edge | Anyone, including bots | Untrusted |
| 2 | Edge to site | The edge, which forwards client IP in a header | Trusted for transport and client IP only, never for content |
| 3 | Site to MAP | A third-party API | Trusted to store leads; responses are parsed, never rendered |
| 4 | Back office to block settings | Authenticated editors | Semi-trusted; settings are validated like input |

## 3. Assets and classification

| Asset | Classification | Why it matters |
|---|---|---|
| Lead fields (name, email, company, message) | **Personal data.** The message is free text, so it is treated as possibly sensitive. | Privacy law, and visitor trust |
| Consent flag and consent text version | Personal data, and legal evidence | Marketing without consent is unlawful |
| MAP API credential | Secret | Lets the holder create or read leads |
| Site availability and MAP quota | Operational | Spam can exhaust the MAP's API limits or fill the CRM |
| A lead the visitor was told was received | **Value** under ES-022 | It must never be lost silently, and never duplicated |

## 4. Threats and mitigations

Each mitigation names the test that will prove it. "Edge" items are Phase 5 infrastructure.

### Spoofing and automated abuse

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| Bots submit spam leads | Three layers. A honeypot field. A signed render token (Data Protection, built in) that carries the render time and rejects submissions faster than 3 s or older than 2 h. Per-client rate limiting (ASP.NET Core rate limiter, built in) of 5 submissions per 10 minutes. The edge WAF bot rules add a fourth. | Unit tests for honeypot, token age and token tampering. Integration test: the 6th submission gets 429. |
| Spam gets through all of that | **Owner decision D1:** add a CAPTCHA (for example Cloudflare Turnstile). It would be a third-party script, so it needs an allowlist entry, a consent review and a new dependency. | n/a until decided |
| Cross-site request forgery posts leads from other sites | Antiforgery token (built in) on the form post. The policy's `form-action 'self'` stops the site's own pages posting elsewhere. | Integration test: a post without the token gets 400. |

### Tampering

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| The visitor changes hidden fields, such as the target MAP list | The server decides the list from the published Form block (by its key), never from the request. The request carries only the block key, and the server loads the block's settings. | Unit test: a forged list ID is ignored. |
| Field injection: HTML or script in fields becomes stored XSS in the CRM | The domain parses every field into precise types (ES-021): an email address, length-limited plain text, with control characters stripped. Anything over the limits is rejected. The site never echoes fields back unencoded. | CsCheck properties on the parser, and a retained corpus (ES-007). |
| An editor sets a block list ID outside the allowlist | List IDs come from an allowlist in configuration. The block stores a key into it, and an unknown key fails validation at render and at submit. | Unit test. |

### Repudiation

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| A visitor disputes giving consent | The MAP stores the consent flag, the consent text version and the submission time. The site keeps no copy. | Contract test: the MAP request carries the consent fields. |
| An outcome cannot be traced across site and MAP | A non-personal correlation ID (the idempotency key) is logged with the outcome and sent to the MAP. | Log test (see Information disclosure). |

### Information disclosure

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| Personal data in logs, traces or error reports (ES-025) | Request bodies and form values are never logged. Telemetry is limited: OpenTelemetry HTTP instrumentation records no bodies or query strings for this route. Exceptions are logged by type and correlation ID only. | Integration test: submit unique marker values, then assert that captured logs and spans contain none of them. |
| Error messages reveal whether an email is already known | The response is the same whether or not the lead exists in the MAP. | Contract test with the stub reporting "duplicate". |
| The MAP credential leaks | The credential lives in Key Vault, read with managed identity (ES-024). It is scoped to "create lead" only. Locally, user secrets or the stub. | Policy test: no MAP key in config; ledger evidence. |
| Interception in transit | HTTPS to the MAP, with certificate validation left on. The edge-to-site hop is covered by DEV-010. | Configuration review. |

### Denial of service

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| Large or many submissions exhaust the site | Request body limit of 16 KB on the route. Field length limits. The rate limiter. The edge WAF. | Integration test: a 17 KB post gets 413. |
| A slow MAP ties up site threads (ES-043, ES-045) | A 5 s deadline per MAP call and 10 s overall. Outbound concurrency to the MAP is capped (bulkhead, 20); excess requests are refused fast with "please try again". | Contract tests with stub delays: timeout and overload. |
| Retry amplification (ES-044) | **The site owns retries:** one retry, with jitter, only with the idempotency key. The browser does not auto-retry. The standard resilience handler in ServiceDefaults adds its own retries, so the MAP client opts out of it and declares its own pipeline. | Contract test counting stub calls on failure. |

### Elevation of privilege

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| The form endpoint reaches more than it should | It is anonymous and has no Umbraco member or back-office access. It calls one MAP endpoint, configured, never taken from input (no SSRF). | Architecture test: the form domain logic has no Umbraco dependency (ES-018). |
| Open redirect after submit | The thank-you view is rendered in place. There is no `returnUrl` parameter. | Unit test. |

## 5. Never lost, never duplicated (ES-022)

- **The site says "thank you" only after the MAP has acknowledged the lead.** Otherwise the visitor sees "we could not send this, please try again", and their input is kept in the page.
- **An idempotency key** (signed, in the render token) identifies the submission. The MAP request carries it, so the MAP or the stub can de-duplicate on it.
- **A timeout is not a failure.** ES-043 says a timeout does not prove the side effect did not happen. After a timeout, the site looks up the key in the MAP before it retries. If the lookup is not possible, the visitor is told the outcome is unknown and asked not to resubmit for a few minutes. The double-submit test covers what happens if they do.
- **Tests:**
  - concurrent double submission of the same key creates one lead;
  - partial failure, where the MAP accepts but the response is lost, is reconciled by lookup;
  - an outage gives a clear error and no thank-you.

**Owner decision D2: what happens when the MAP is down?**
- **Option A (recommended, and the plan's default).** Store nothing. The visitor retries later. A lead can be lost if the visitor gives up, but the site never claims to have received it.
- **Option B.** Keep a short-lived, encrypted outbox (for example a storage queue with a 7-day TTL) and acknowledge on enqueue. This loses nothing during outages, but brings ES-058 and ES-059 into scope: classification, retention, deletion across replicas and backups, and restore tests.

## 6. Privacy (ES-025, ES-058)

- The privacy notice link and the consent text sit next to the consent checkbox. The consent text version is stored with the lead in the MAP.
- **Purpose:** sales follow-up, plus marketing only with consent.
- **Retention** is the MAP's policy. The site retains nothing; logs carry no personal data.
- **No third-party scripts on the form page**, so no tracking pixels on submission (the ADR 0008 allowlist stays empty).

## 7. Residual risks and open decisions

| ID | Item | Owner action |
|---|---|---|
| D1 | CAPTCHA or not | Decide. Default is no CAPTCHA, with the bot layers above. |
| D2 | Outbox or no outbox during a MAP outage | Decide. Default is Option A. |
| D3 | Rate-limit numbers (5 per 10 minutes per client) and field limits | Confirm or adjust. |
| D4 | Which real MAP, and its API's idempotency and lookup support | Needed before go-live. The design assumes lookup by an external ID. |
| R1 | Determined human spammers will get through | Accepted as residual. CRM-side de-duplication and list hygiene handle it. |
| R2 | The edge WAF and HSTS do not exist until Phase 5 | Tracked by DEV-010. Forms are not enabled on a public environment before Phase 5. |

## 8. What approval unlocks

Once approved, Phase 4 implements this model test-first, in this order:
1. the domain parser and properties;
2. the MAP client and contract tests against WireMock;
3. the endpoint with antiforgery, rate limit and size limit;
4. the component and its accessibility tests.

Each mitigation above is linked to its test in the PR. A change to the field set, to storage (D2) or to third parties (D1) reopens this model.
