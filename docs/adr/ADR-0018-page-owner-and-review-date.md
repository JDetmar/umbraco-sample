# 0018. Page owner and review cycle, inherited down the tree, with a weekly digest to each owner

- Status: Proposed
- Date: 2026-11-25; revised 2026-11-26 for a site of about 1,500 pages
- Decider: owner
- Clauses: ES-002, ES-019, ES-020, ES-023, ES-047, ES-058; framework GOV-02, GOV-03

## Context

Every page needs a named content owner and a review or expiry date (GOV-02, Must).
Pages that are ownerless or past review must be flagged automatically (GOV-03, Should).

The site has about 1,500 pages.
That number decides the design:

- On a 12-month cycle, about 125 pages come due each month, about 30 a week.
  Nobody tracks that from memory, so the owner and the due date are what make review happen at all.
- Setting an owner and a date by hand on 1,500 pages will not happen.
  A design that needs it starts with about 1,500 errors and stays there.
- If every page gets the same first date, all 1,500 fall due in the same week.
- A report that names 20 pages per finding is not a work list when 1,400 pages share the finding.
- A check that someone has to remember to open is not automatic.
  Owners need to be told which of their pages are due.

The first version of this ADR (one owner and one review date on each page, flagged by an on-demand health check, naming up to 20 pages) is built on `main`.
It is sound for a handful of pages but fails every point above, so this revision replaces it before it is accepted.

## Alternatives considered

**Where the owner and cycle come from**

- **Set on every page.** Simple, and what the first version does.
  At 1,500 pages the backfill never finishes, and a reorganisation means editing hundreds of pages.
- **Inherited from the nearest ancestor that sets them.** A few dozen section pages cover the site, and a page can still set its own.
  The effective value is no longer visible on the page itself, so the report must show where it came from.
  Chosen.

**What decides when a page is due**

- **A fixed review date on each page.** Needs a date on every page, and the backfill bunches them.
- **A review cycle plus a last-reviewed date.** The cycle is inherited like the owner, the page records when it was last reviewed, and the due date is computed.
  Chosen.
- **The last publish date as the last review.** Needs no new field, but a typo fix would count as a review.
  Rejected.

**How a page that has never been reviewed gets its first due date**

- **Today plus the cycle.** Every page falls due on the same day.
- **Spread across the first cycle by a stable hash of the page key.** Each page gets a fixed first due date somewhere in the first cycle after governance starts, so about 1/52 of the site falls due each week, and the date does not move between runs.
  Chosen.

**Where the full list lives**

- **A custom back-office view.** The most usable, but Umbraco 17 back-office extensions are JavaScript, which adds a front-end build to the back office (the reason the first version used a health check).
- **A CSV export behind back-office authorization, linked from the health check.** Opens in a spreadsheet, filters by owner and section, and needs no back-office front-end code.
  Chosen.

**How owners are told**

- **Umbraco's health-check notifications.** Built in, but they send one summary to one address, not each owner's own pages.
- **A weekly digest per owner from a recurring background job.** Each owner gets only their due and overdue pages.
  Chosen; the delivery channel is an open decision below.

## Decision

**Schema.**
The `contentGovernance` composition, on every page type, has three optional properties in the Governance group:

- **Content owner**: a user picker, as before.
- **Review cycle**: a dropdown of 3, 6, 12 or 24 months.
- **Last reviewed**: a date the owner sets when they have checked the page.

The single **Review date** property is removed.
The home page is the root of inheritance; the health check reports an error while the home page has no owner or no cycle.

**Rules, in the domain (ES-020).**
`UmbracoSample.Domain.Governance` holds pure functions with no CMS types, property-tested (ES-002) with the clock passed in:

- **Effective owner and cycle**: the value on the page if set, otherwise the nearest ancestor's.
  The result records which page it came from.
- **Due date**: last reviewed plus the effective cycle.
  With no last-reviewed date, the first due date is the governance start date plus a fraction of the cycle given by a stable hash of the page key.
  The start date is configuration, so each environment sets when the clock starts.
- **Findings**: no effective owner; the owner's account is disabled or deleted (counted as no owner, because the page is effectively orphaned); overdue; due within 30 days.

Properties the tests cover include:

- inheritance from the nearest ancestor that sets a value;
- the first due date always falls inside the first cycle;
- the spread of first due dates is close to uniform;
- the due date never changes between runs for the same input.

**Reading.**
The check walks the published content cache once, from the root down, passing the inherited owner and cycle to each child.
The walk visits each published page once, so its work is bounded by the size of the tree (ES-047).
It no longer loads pages through the content service.

**Reporting.**

- The health check, under Settings, Health Check, Content, reports counts per finding, the 10 sections and 10 owners with the most findings, and a link to the export.
- The export, `GET /umbraco/management/api/v1/governance/pages.csv`, needs a back-office user with access to the Content section.
  It returns one row per published page: name, URL, section, effective owner and where it came from, cycle, last reviewed, due date and findings.
  Owners appear by name, never by email address.

**Digest.**

- A recurring background job runs weekly, on the scheduling server only (ADR 0015 keeps one).
- It sends each active owner one message listing their overdue pages and pages due in the next 30 days, at most 50 pages, with a link to the export for the rest.
- Pages with no effective owner go to a governance contact set per environment.
- It is off unless its delivery setting is configured, so dev and CI send nothing by default.
- A run records the week it covered, so a restart in the same week does not send the digest twice.

**Data.**
The owner is a reference to a back-office user that Umbraco already stores.
The digest reads the user's email address only to send the message, and stores nothing new about the user.
The run record holds only the week and the count sent.

**Open decisions for the owner.**
These must be settled before the digest is built:

1. **Delivery channel.**
   The site has no mail delivery today.
   Recommended: Azure Communication Services Email, called with the site's managed identity, so no SMTP password is stored (ES-024).
   It adds an Azure resource per environment and a NuGet package, which needs an entry in `docs/dependencies.md` and your approval (ES-015).
   The alternative, SMTP with a stored credential in Key Vault, needs no package but adds a secret.
2. **The governance contact** who receives the digest for ownerless pages, per environment.
3. **The default cycle**, if the home page should start with one other than 12 months.

**Before implementation.**
The export endpoint is a new authenticated route, and the digest sends to an external service.
An update to threat model 0002 covering both comes before the code (ES-023).

## Consequences

- **GOV-02 is met by construction once the home page has an owner and a cycle.**
  Every page then has an effective owner and a due date, and the backfill is a few dozen section pages, not 1,500.
- **GOV-03 is met once the digest is live.**
  Until a delivery channel is approved and configured, the flag is on demand only.
  `docs/traceability.md` keeps GOV-03 as "partly" until the digest runs in dev.
- An owner leaving shows up immediately: disabling their account makes all their pages ownerless in the next report and digest.
- An inherited owner can surprise the people who own a section.
  The export shows where each owner came from, and the digest goes to the effective owner, so a section owner sees everything they inherit.
- Removing **Review date** is a schema change.
  No published page uses it outside the synthetic seed content, so it needs no data migration; the uSync files change with the code.
- Marking a page as reviewed means setting **Last reviewed** and publishing.
  A one-click "mark reviewed" action would need back-office front-end code; revisit it if editors find the field awkward.
- The hash spread applies only to a page's first cycle.
  After that, due dates follow when each page was actually reviewed, so they stay spread unless editors review in large batches.
- The check is bounded by the tree size.
  At ten times the current site it is still one pass over the cache, but the export and digest should be measured again before the site grows that much.
