# 0018. Page owner and review date, flagged by a back-office health check

- Status: Proposed
- Date: 2026-11-25
- Decider: owner
- Clauses: ES-019, ES-020, ES-038; framework GOV-02, GOV-03

## Context

Every page needs a named content owner and a review date (GOV-02), and pages that are ownerless or past review must be flagged automatically (GOV-03). The flag has to reach editors where they work, without new dependencies or a custom front-end build in the back office.

## Alternatives considered

- **Owner as free text or an email address.** No coupling to back-office users, but nothing stops a typo or a person who has left, and it cannot later drive notifications to a user.
- **Owner as a back-office user (user picker).** Names a real account that can be disabled when someone leaves, and can later drive notifications. Chosen.
- **Flag in a custom back-office dashboard.** The most visible, but Umbraco 17 dashboards are JavaScript extensions, which would add a front-end build to the back office.
- **Flag in Umbraco's health checks.** A C# class Umbraco discovers by its attribute, shown under Settings, Health Check, with no front-end code. Health checks can also be scheduled to send notifications. Chosen.

## Decision

- A `contentGovernance` composition adds **Content owner** (a user picker) and **Review date** (the existing date picker) to every page type, in a Governance group. Both are optional in the editor, so a draft can be saved without them; the health check flags their absence.
- The rule lives in the domain (`PageReview.Assess`): no owner, no review date, overdue (the date has passed), or due within 30 days. It is pure and property-tested (ES-020).
- `ContentGovernanceHealthCheck` reads every published page through the content service and reports one result per kind of finding, naming up to 20 pages each: errors for no owner, no review date or overdue, a warning for due soon.

## Consequences

- Editors see the state of the site in the back office on demand. Nothing runs on page requests.
- The seeded pages have no owner or review date, so dev shows them flagged until an editor sets them; that is the check working, not a fault.
- Scheduled health-check notifications (email) are not configured; they need a mail setting per environment and are a follow-up.
- A page whose owner's account is disabled still counts as owned. Checking the account state is a follow-up if it matters.
