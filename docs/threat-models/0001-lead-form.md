# Threat model 0001: lead form forwarded to the marketing automation platform

- Status: **Approved by the owner on 2026-11-24, with decisions D1 to D4 recorded below (ES-023).**
- Reviewed again by the owner on 2026-11-26 before Phase 4 started (project thread): no changes.
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
  Editors choose only the heading, the intro text, the thank-you message, the MAP list the lead goes to (from an allowlisted set of keys) and the consent version (from a fixed set). The consent wording for each version is fixed in configuration, not in the block, so the stored version always names the exact wording the visitor saw (repudiation).

## 2. Data flow and trust boundaries

```
Visitor browser ──(1)──► Edge (Front Door + WAF) ──(2)──► Site container ──(3)──► MAP API
                                                            │
Editor (back office) ──(4)── configures the Form block ─────┘
```

| # | Boundary | Who is on the other side | Trust |
|---|---|---|---|
| 1 | Internet to edge | Anyone, including bots | Untrusted |
| 2 | Edge to site | The edge, which forwards client IP in a header | Trusted for transport, and for client IP only on requests that carry this deployment's Front Door ID header; never for content |
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
| Bots submit spam leads | Three layers. A honeypot field. A signed render token (Data Protection, built in) that carries the render time and rejects submissions faster than 3 s or older than 2 h. Per-client rate limiting (ASP.NET Core rate limiter, built in) of 3 submissions per 10 minutes (D3); every attempt counts, including rejected ones. The client is the edge's client IP header only when the request carries this deployment's Front Door ID header; otherwise it is the connection IP, so a forged `X-Forwarded-For` is ignored. The limiter is in memory, so it counts per replica: with N replicas a client gets up to N×3. The edge WAF bot rules add a fourth layer and are the global limit. | Unit tests for honeypot, token age and token tampering, using `TimeProvider` (FakeTimeProvider) for time. Integration test on its own isolated rate-limit policy: the 4th submission gets 429; a spoofed `X-Forwarded-For` without the Front Door ID is ignored; two distinct clients are counted separately. |
| Spam gets through all of that | **D1, decided 2026-11-24:** a CAPTCHA, switched per environment by a `Forms:Captcha:Enabled` setting that is off by default. **Local and CI: off**, because automated tests cannot solve it. **Dev and staging: on, with the provider's always-pass test keys**, so the widget, the policy entries and the server-side verification are exercised before production. **Prod: on, with real keys** (secret key in Key Vault, ES-024). The provider is not chosen yet, so for now the setting stays off everywhere and nothing is configured. Choosing it makes the provider a new dependency, with a third-party allowlist entry (ADR 0008) and a consent review; it also needs a verification deadline and a decision on whether a provider outage fails open or closed. | Unit tests, with a fake verifier behind an `ICaptchaVerifier` interface: off by default, and the form renders and submits with no CAPTCHA markup or script; with it on, the token is verified server-side and a missing or failed token is rejected; with it on but no provider configured, the site refuses to start (fail closed). The dev smoke test that the widget loads under the policy is deferred until a provider is chosen. |
| Cross-site request forgery posts leads from other sites | Antiforgery token (built in) on the form post. The policy's `form-action 'self'` stops the site's own pages posting elsewhere. | Integration test: a post without the token gets 400. |

### Tampering

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| The visitor changes hidden fields, such as the target MAP list | The server decides the list from the published Form block (by its key), never from the request. The request carries only the block key, and the server loads the block's settings. | Unit test: a forged list ID is ignored. |
| Field injection: HTML or script in fields becomes stored XSS in the CRM | The domain parses every field into precise types (ES-021): an email address, length-limited plain text, with control characters stripped. Anything over the limits is rejected. The site never echoes fields back unencoded. | CsCheck properties on the parser, and a retained corpus (ES-007). |
| An editor sets a block list ID outside the allowlist | List IDs come from an allowlist in configuration. The block stores a key into it, and an unknown key fails validation at render and at submit. | Unit test. |
| A lead without marketing consent lands on a marketing list | Each allowlisted list is tagged with its purpose (`sales` or `marketing`). The server routes a lead to a `marketing` list only when the consent box is ticked; without consent it goes to the block's sales list or, if the block has none, is sent with no list. | Unit test: a lead without consent never reaches a `marketing` list. |

### Repudiation

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| A visitor disputes giving consent | The MAP stores the consent flag, the consent text version and the submission time. The site keeps no copy. | Contract test: the MAP request carries the consent fields. |
| An outcome cannot be traced across site and MAP | The site logs a separate random correlation ID (the trace ID) with the outcome. It is never sent to the MAP: the MAP client turns off trace context propagation, so its requests carry no `traceparent` or `tracestate` header, and the logs cannot be joined to a lead. The idempotency key goes to the MAP only in the request body or a header, never in a URL path or query (including the lookup), so client spans cannot record it; it is never logged. Tracing one lead end to end therefore needs the MAP's own audit log. This is a design trade-off and departs from no clause. | Contract test: MAP requests carry no `traceparent` or `tracestate`, and no request URL contains the key. Log test (see Information disclosure): the idempotency key appears in no log and no span. |

### Information disclosure

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| Personal data in logs, traces or error reports (ES-025) | Request bodies and form values are never logged. Telemetry is limited: OpenTelemetry HTTP instrumentation records no bodies or query strings for this route. Exceptions are logged by type and correlation ID only. MAP response bodies are never logged, because a MAP error can echo the input. The client IP is used for rate limiting and never logged. | Integration test: submit unique marker values, then assert that captured logs and spans contain none of them, nor the client IP, on the success path and on every failure path: validation failure, MAP 4xx and 5xx that echo the input, timeout, and a response the parser rejects. |
| Error messages reveal whether an email is already known | The response is the same whether or not the lead exists in the MAP. | Contract test with the stub reporting "duplicate". |
| The MAP credential leaks | The credential lives in Key Vault, read with managed identity (ES-024). It is scoped to "create lead" only. Locally, user secrets or the stub. | Policy test: no MAP key in config; ledger evidence. |
| Interception in transit | HTTPS to the MAP, with certificate validation left on. Front Door to the Container Apps ingress is HTTPS; inside the Container Apps environment the ingress forwards plain HTTP to the container. That last hop is recorded as gap DEV-013 for the owner. HSTS at the edge is DEV-010. | Configuration review; DEV-013. |

### Denial of service

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| Large or many submissions exhaust the site | Request body limit of 16 KB on the route. Field length limits. The rate limiter. The edge WAF. | Integration test: a 17 KB post gets 413. |
| A slow MAP ties up site threads (ES-043, ES-045) | A 10 s budget per submission: the create call has 5 s, and the lookup after a timeout gets what remains, at most 5 s. An exhausted budget gives "outcome unknown", never "failed". The request's `RequestAborted` token is linked into every MAP call, so a visitor who disconnects cancels it; a cancellation after the request was sent is also "outcome unknown" and is reconciled by the next submission's idempotency key. Outbound concurrency to the MAP is capped (bulkhead, 20); excess requests are refused fast with "please try again". The bulkhead is per replica, so the outbound cap is N×20; it must stay under the MAP's API quota, which is checked when D4 is reopened. | Contract tests with stub delays: timeout, overload, exhausted budget, and a client disconnect mid-call (outcome unknown, the stub saw at most one create). |
| Retry amplification (ES-044) | **The site owns retries:** one retry, with jitter, only with the idempotency key. The browser does not auto-retry. The standard resilience handler in ServiceDefaults adds its own retries, so the MAP client opts out of it and declares its own pipeline. | Contract test counting stub calls on failure. |

### Elevation of privilege

| Threat | Mitigation | Evidence (planned) |
|---|---|---|
| The form endpoint reaches more than it should | It is anonymous and has no Umbraco member or back-office access. It calls one MAP endpoint, configured, never taken from input (no SSRF). | Architecture test: the form domain logic has no Umbraco dependency (ES-018). |
| Open redirect after submit | The thank-you view is rendered in place. There is no `returnUrl` parameter. | Unit test. |

## 5. Never lost, never duplicated (ES-022)

- **The site says "thank you" only after the MAP has acknowledged the lead.** Otherwise the visitor sees "we could not send this, please try again", and their input is kept in the page.
- **An idempotency key** (signed, in the render token) identifies the submission. The form page is sent with `Cache-Control: private, no-store`, bypasses the edge cache and is excluded from the site's output cache, so every render gets its own key; a cached page would give every visitor the same key and the MAP would merge their leads. Tests: two renders carry different keys, and the form page has the no-store header. The MAP request carries the idempotency key, so the MAP or the stub can de-duplicate on it.
- **A timeout is not a failure.** ES-043 says a timeout does not prove the side effect did not happen. After a timeout, the site looks up the key in the MAP before it retries. If the lookup is not possible, the visitor is told the outcome is unknown and asked not to resubmit for a few minutes. The double-submit test covers what happens if they do.
- **Tests:**
  - concurrent double submission of the same key creates one lead;
  - partial failure, where the MAP accepts but the response is lost, is reconciled by lookup;
  - an outage gives a clear error and no thank-you.

**D2, decided 2026-11-24: what happens when the MAP is down.** The owner chose Option A.
- **Option A (chosen).** Store nothing. The visitor retries later. A lead can be lost if the visitor gives up, but the site never claims to have received it.
- **Option B (not chosen).** Keep a short-lived, encrypted outbox (for example a storage queue with a 7-day TTL) and acknowledge on enqueue. This loses nothing during outages, but brings ES-058 and ES-059 into scope: classification, retention, deletion across replicas and backups, and restore tests.

## 6. Privacy (ES-025, ES-058)

- The privacy notice link and the consent text sit next to the consent checkbox. The consent text version is stored with the lead in the MAP.
- **Purpose:** sales follow-up, plus marketing only with consent.
- **Retention** is the MAP's policy. The site retains nothing; logs carry no personal data.
- **No third-party scripts on the form page** until the D1 CAPTCHA provider is chosen; that provider then gets an ADR 0008 allowlist entry. No tracking pixels on submission.
- **The idempotency key is pseudonymous:** the MAP can link it to a lead. It is never logged by the site, so it carries no extra retention.
- **Dev and staging use the WireMock stub or a MAP sandbox only**, never a production MAP account (ES-027).

## 7. Residual risks and open decisions

| ID | Item | Owner action |
|---|---|---|
| D1 | CAPTCHA | **Decided:** off locally and in CI; on in dev and staging with test keys and in prod with real keys, once the owner chooses and configures a provider. Until then it is off everywhere. |
| D2 | Outbox or no outbox during a MAP outage | **Decided:** no outbox. The site stores nothing and the visitor retries. |
| D3 | Rate-limit numbers and field limits | **Decided:** 3 submissions per 10 minutes per client (per replica), a 16 KB body, and field limits of name ≤ 100, email ≤ 254, company ≤ 100 and message ≤ 2,000 characters. The numbers are configuration. |
| D4 | Which real MAP | **Decided for now:** design against a generic, documented WireMock stub that supports idempotency keys and lookup by external ID. Choosing a real platform reopens this model and adds contract tests against its API. The chosen platform must support idempotency keys (or an external ID with de-duplication) and lookup by it, and its API quota must exceed the outbound cap. |
| R1 | Determined human spammers will get through | Not a departure from the standard: no clause requires stopping human spam. Recorded as residual; CRM-side de-duplication and list hygiene handle it. |
| R2 | The edge WAF and HSTS do not exist until Phase 5 | Tracked by DEV-010. Forms are not enabled on a public environment before Phase 5. |
| R3 | Data Protection keys live in the container until a shared key ring exists | A render token signed on one replica fails on another, and every token on screen fails after a restart or new revision. Phase 5 persists the key ring to shared storage before the form is enabled on any deployed environment, even with one replica. **Built (ADR 0021):** the key ring is one blob wrapped by a Key Vault key, and outside development the form refuses to start without it. |
| R4 | Edge-to-container plain HTTP inside the Container Apps environment | Recorded as gap DEV-013 for the owner to decide. |

## 8. What approval unlocks

Once approved, Phase 4 implements this model test-first, in this order:
1. the domain parser and properties;
2. the MAP client and contract tests against WireMock;
3. the endpoint with antiforgery, rate limit and size limit;
4. the component and its accessibility tests.

**Progress (recorded by the implementer, not a change to the model).** Steps 1 and 2 are done. Step 3 is split in two. Step 3a (the handler, the render token, the client identity, the rate-limit policy and the D1 switch) is framework-only and tested without the site. Step 3b, the site endpoint, moves into step 4, because the endpoint is the Form block's own post-back. It brings the antiforgery (400), 16 KB body limit (413) and no-store tests, the in-page re-render that reuses the posted render token, and the binding test that an extra `listId` field is ignored. Step 4 is not done until those tests exist.

Step 4 progress: the Form block, its post-back and those tests exist (`tests/UmbracoSample.Integration.Tests/LeadFormTests.cs`, against the real site and an in-memory MAP stub). Two details differ from the sketch above. The checks run as body limit, then rate limit, then antiforgery, because the first two are middleware and antiforgery is an MVC filter; every attempt still counts against the limit. The form page carries `no-store` (antiforgery sets `no-cache, no-store`). The consent wording is fixed per version in configuration, not edited in the block (section 1). The form is off (`Leads:Enabled`) on every deployed environment until R2 and R3 are closed; the end-to-end accessibility tests follow. The 16 KB cap also covers Umbraco's direct surface route (`/umbraco/surface/...`), and the form refuses to start alongside Umbraco's website output cache, which would share one render token between visitors. Known limit, for the owner: the cap counts URL-encoded bytes, and a non-Latin character encodes to 6 to 12 bytes, so a message near the 2,000-character limit in such a script can exceed 16 KB and get a 413 that says the submission is too large.

Each mitigation above is linked to its test in the PR. A change to the field set, to storage (D2) or to third parties (including choosing the D1 CAPTCHA provider, its verification deadline and its fail-open or fail-closed behaviour) reopens this model.
