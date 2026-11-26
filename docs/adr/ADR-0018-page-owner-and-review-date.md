# 0018. Page owner and review cycle, inherited down the tree

- Status: Accepted by the owner on 2026-11-26 (project thread), with the 12-month default cycle as written
- Date: 2026-11-25; revised 2026-11-26 for a site of about 1,500 pages
- Decider: owner
- Clauses: ES-002, ES-019, ES-020, ES-023, ES-025, ES-047, ES-058, ES-059; framework GOV-02, GOV-03

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
- **Spread across the first cycle by a stable hash of the page key.** Each page gets a fixed first due date somewhere in the first cycle after governance starts, so on a 12-month cycle about 1/52 of those pages falls due each week, and the date does not move between runs.
  Chosen.

**Where the full list lives**

- **A custom back-office view.** The most usable, but Umbraco 17 back-office extensions are JavaScript, which adds a front-end build to the back office (the reason the first version used a health check).
- **A CSV export behind back-office authorization, linked from the health check.** Opens in a spreadsheet, filters by owner and section, and needs no back-office front-end code.
  Chosen.

**How owners are told**

- **Umbraco's health-check notifications.** Built in, but they send one summary to one address, not each owner's own pages.
- **A weekly digest per owner from a recurring background job.** Each owner gets only their due and overdue pages.
  Deferred: the owner decided on 2026-11-26 to send no email for now, because the site has no mail delivery and adding one needs a new service and either a package or a stored secret. Owners use the health check and the export until then.

## Decision

None of this is built yet.
`main` still has the first version: a fixed review date on each page, `PageReview.Assess`, and a `GovernanceReport` that names up to 20 pages per finding.
The sections below describe what replaces it.

**Schema.**
The `contentGovernance` composition, on every page type, has three optional properties in the Governance group:

- **Content owner**: a user picker, as before.
- **Review cycle**: a dropdown of 3, 6, 12 or 24 months.
- **Last reviewed**: a date the owner sets when they have checked the page.

The single **Review date** property is removed.
The home page is the root of inheritance; the health check reports an error while the home page has no owner.
A page with no cycle on itself or any ancestor uses the default cycle (12 months, below), so a missing cycle is never an error.

**Rules, in the domain (ES-020).**
`UmbracoSample.Domain.Governance` holds pure functions with no CMS types, property-tested (ES-002) with the clock passed in:

- **Effective owner and cycle**: the value on the page if set, otherwise the nearest ancestor's.
  The result records which page it came from.
- **Due date**: last reviewed plus the effective cycle.
  With no last-reviewed date, the first due date is the later of the governance start date and the page's creation date, plus a fraction of the cycle given by a stable hash of the page key.
  A page created after governance starts therefore gets its first due date inside its own first cycle, never in the past.
  The start date is configuration, so each environment sets when the clock starts; while it is unset the health check reports an error and computes no first due dates.
- **Findings**: no effective owner; the owner's account is disabled or deleted (counted as no owner, because the page is effectively orphaned; the page does not fall back to an ancestor's owner, so the gap stays visible until someone is named); overdue; due within 30 days.

Properties the tests cover include:

- inheritance from the nearest ancestor that sets a value;
- the first due date always falls inside the first cycle after the later of the start date and the page's creation date;
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

**Digest (deferred).**
Not built (owner decision, 2026-11-26). When it is, it runs weekly on the scheduling server only (ADR 0015), sends each active owner their overdue pages and those due in the next 30 days (at most 50, with a link to the export), sends ownerless pages to a governance contact per environment, and is off unless delivery is configured. Choosing the delivery channel and the contact is then a new decision.

**Data.**
The owner is a reference to a back-office user that Umbraco already stores.
Nothing new about the user is stored.

**Default cycle.**
12 months, accepted by the owner with this record.

**Before implementation.**
The export endpoint is a new authenticated route.
Threat model 0002 already covers the back office as a trust boundary, so it is extended rather than starting a new one: an update covering the export (authorization, what each row reveals, and that it lists names but no email addresses, ES-025, ES-059) comes before the code (ES-023).

## Consequences

- **GOV-02 is met by construction once the home page has an owner and a cycle.**
  Every page then has an effective owner and a due date, and the backfill is a few dozen section pages, not 1,500.
- **GOV-03 stays partly met.**
  Once built, findings are computed automatically for the health check and export, but nobody is told unless they look: the digest is deferred.
  `docs/traceability.md` keeps GOV-03 as "partly" until owners are notified.
- An owner leaving shows up immediately: disabling their account makes all their pages ownerless in the next report.
- An inherited owner can surprise the people who own a section.
  The export shows where each owner came from, so a section owner can filter for everything they inherit.
- Removing **Review date** is a schema change.
  Only the synthetic seed content uses it today, so the change needs no data migration script: the uSync files and seed content change with the code.
  A site with real review dates would need them copied into **Last reviewed** (date minus the cycle) before the property is removed.
- Marking a page as reviewed means setting **Last reviewed** and publishing.
  A one-click "mark reviewed" action would need back-office front-end code; revisit it if editors find the field awkward.
- The hash spread applies only to a page's first cycle.
  After that, due dates follow when each page was actually reviewed, so they stay spread unless editors review in large batches.
- The check is bounded by the tree size.
  At ten times the current site it is still one pass over the cache, but the export should be measured again before the site grows that much.
