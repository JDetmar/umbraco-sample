# 0005. Test stack

- Status: Accepted
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-001 to ES-007, ES-018, ES-036

## Context

Automated checks replace manual testing as the source of confidence. The standard asks for behaviour-focused tests with real oracles (ES-001), coverage as a diagnostic only (ES-002), fakes before mocks (ES-004), real infrastructure in integration tests (ES-006), executable architecture rules (ES-018) and automated accessibility checks (ES-036).

## Alternatives considered

- **MSTest or NUnit:** both capable; xUnit v3 has the strongest fit with Microsoft.Testing.Platform and Aspire's test templates.
- **FluentAssertions:** rejected; commercial licence from version 8.
- **Moq or NSubstitute by default:** rejected as a default; ES-004 prefers behavioural fakes.
- **Cypress for browser tests:** Playwright covers more browsers and has built-in visual comparison.

## Decision

| Need | Tool |
|---|---|
| Unit and integration runner | xUnit v3 on Microsoft.Testing.Platform |
| Assertions | Shouldly |
| Property-based tests | CsCheck |
| Mutation tests (scheduled lane) | Stryker.NET |
| Integration tests against real infrastructure | Aspire.Hosting.Testing |
| External HTTP stub | WireMock.Net |
| Architecture rules | NetArchTest.Rules |
| Browser, visual regression | Playwright |
| Accessibility | axe-core through @axe-core/playwright |
| Performance budgets | Lighthouse CI |

Tests that need containers carry `[Trait("Category", "Integration")]` and run in their own CI lane; `make test` excludes them to keep the fast lane near ten minutes (ES-012).

## Consequences

- Coverage is reported but only critical modules carry a floor (ES-002); this departs from the framework's TST-02, and the plan records why.
- NetArchTest has low release activity; ArchUnitNET is the named fallback.
