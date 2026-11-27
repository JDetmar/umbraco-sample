# Form

A lead form that sends a visitor's details to the marketing platform (threat model 0001). Element type `formBlock`, view `Views/Partials/Components/Form.cshtml`, model `FormModel`, post-back `LeadFormController` (`src/UmbracoSample.Web/Leads`).

| Property | Alias | Type | Required | Notes |
|---|---|---|---|---|
| Heading | `heading` | Textstring | Yes | Up to 120 characters; rendered as `h2` and names the form's region |
| Intro | `intro` | Textarea | No | Up to 500 characters |
| List | `list` | Dropdown (Lead list) | Yes | A key from `Leads:Lists`; only configuration knows the platform's list ID. A marketing list gets the lead only with consent |
| Consent version | `consentVersion` | Dropdown (Consent version) | Yes | A key from `Leads:Consent`, which holds the fixed wording for that version |
| Thank-you message | `thankYou` | Textstring | Yes | Up to 200 characters; shown only after the platform confirms the lead |

The visitor's fields are fixed in code: name and email (required), company and message (optional), and an unticked marketing consent box. A hidden honeypot field catches bots.

**Not rendered when** a required property is empty, the form is switched off (`Leads:Enabled`, off by default and on every deployed environment until the edge and the shared key ring exist), or the consent version has no configured wording.

**Configuration** (`appsettings.json`, synthetic values):

```json
"Leads": {
  "Enabled": false,
  "Lists": { "general-enquiry": { "ListId": "...", "Purpose": "Sales" }, "newsletter": { "ListId": "...", "Purpose": "Marketing" } },
  "Consent": { "2026-11": "The wording the visitor sees" }
}
```

A test keeps the back-office dropdowns and these keys identical. Changing consent wording means adding a new version, never editing an old one.

**How a post is handled**, in order: the 16 KB front-end body limit (413), the `lead-form` rate limit of 3 posts per 10 minutes per client (429), antiforgery (400), then `LeadSubmissionHandler` (render token, bot screen, block lookup on the posted page, field rules, list routing, platform call). The page re-renders in place with the result and the visitor's input. Every form page and response carries `no-store`.

**Render token.** Each page view issues a new token, with its own idempotency key. A re-render after an invalid post, an outage or an unknown outcome keeps the posted token, so a resubmission cannot create a second lead. A refused post gets a fresh token.

**Results shown:** thank-you message; field errors with a summary linking to each field; "please try again"; "contact us another way"; "please wait before you send again"; "please reload the page".

**Fixtures:** `/fixture-form/` has one form (general enquiry, consent version `2026-11`). Locally and in the integration tests the form is on and posts to the in-memory MAP stub (`tests/UmbracoSample.MapStub`).

**Tests:** `FormBlockSchemaTests` and `FormBlockTests` (fast), `LeadFormTests` (integration: 400, 413, 429, no-store, token per render and reuse, posted `listId` ignored, double submit, outage, no visitor values in logs or spans).
