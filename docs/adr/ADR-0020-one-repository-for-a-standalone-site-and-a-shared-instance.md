# 0020. One repository for a standalone site and a later shared multi-site instance

- Status: Proposed
- Date: 2026-11-26
- Decider: owner
- Clauses: ES-001, ES-013, ES-018, ES-019, ES-028, ES-049, ES-052; framework ARC-01, ARC-05, TST-08, TST-10

## Context

The code is meant to serve more than one site. Two hosting shapes are expected:

- **A standalone site.** One site per Umbraco instance, with its own schema, release cadence and blast radius. This repository's site is one.
- **A shared multi-site instance.** Several smaller sites as root nodes of one Umbraco instance, with one shared set of document types and one release cadence.

One development team works on all of them. Fixes to the shared parts (security headers, the redirect parser, components, governance, CI) must reach every site without being copied by hand.

Until this decision, the site assumed it was the only one:
- its name was hardcoded in the layout and in Pulumi resource names;
- the redirect map and the third-party allowlist applied to every host;
- component, security and governance code lived inside the Web project.

The risk runs both ways. Copying code per site lets the copies drift apart. Building for sites that don't exist yet adds options and plugin points nobody uses, and every one of them must be tested and maintained (ES-001).

## Alternatives considered

1. **A template repository, or a `dotnet new` template, copied once per site.** Fast to start, and every site is independent. But every shared fix has to be ported to each copy, and the copies drift. That suits a handful of unrelated sites, not one team running many.
2. **Shared NuGet packages and one thin repository per site.** Versions keep sites independent, and fixes ship centrally. The costs are a package feed, a release process for each library, and an upgrade chore for every site on every release. That overhead pays off across separate teams, not for one team that can change every site in the same pull request.
3. **One repository: shared libraries, a standalone host and, later, a multi-site host, side by side (chosen).** A shared change and its effect on every site are reviewed and tested together in one pull request, with no version skew. The costs are that CI must learn to build and deploy only what a change affects, and that one bad shared change can reach several sites at once.
4. **Everything in one multi-site instance, the standalone site included.** One upgrade and one deploy for everything. But the standalone site would lose its own release gate and blast radius, and a shared instance carries the traffic of all its sites (DEV-015 limits it to one replica today).

## Decision

**Shape.** This repository grows into a monorepo:
- **Shared libraries** in `src/`, holding no content and no site-specific values:
  - `UmbracoSample.Domain` (pure logic, ADR 0009);
  - `UmbracoSample.Components` (component view models and the mapping from blocks);
  - `UmbracoSample.Security` (headers, content security policy, allowlist validation, ADR 0008);
  - `UmbracoSample.Governance` (the content governance health check, ADR 0018);
  - `UmbracoSample.ServiceDefaults`;
  - the Pulumi site definition.
- **A standalone host**, `UmbracoSample.Web`, which holds this site's schema, content, views, redirect map and allowlist.
- **A multi-site host**, added only when a trigger below fires.

**Seams added now.** These are cheap, and each one has a test. Anything a site needs beyond them waits for a real site that needs it.
- **Site name.** The header brand comes from the site root's title, as the structured data's `WebSite` name already does, so each root node names its own site. No new property: a separate brand field waits until a site needs a brand that differs from its root's title.
- **Infrastructure.** Pulumi takes a `siteSlug` (default `umbraco-sample`) that names the site's Azure resources and feeds the hash behind their globally unique names. With the default, every existing name stays the same, so the dev stack sees no replacement.
- **Redirects per host.** `redirects.csv` stays the map for any host without its own. A host with its own rules gets `redirects.<host>.csv`, which replaces the default for that host instead of adding to it, so one site's rules never apply to another. Every file is parsed and validated at startup, as before (ADR 0009).
- **Allowlist per host.** An allowlist entry may name the hosts it applies to. With no hosts it applies to every site. The content security policy is built per host from the entries that apply. An entry stays a reviewed code change (ADR 0008).

**How sites differ.** A site customises through two mechanisms only:
- **Design tokens** (ADR 0007): a theme is a set of token files, and contrast tests run per theme.
- **Razor view overrides**: when the shared views move into a Razor class library, a host overrides a view by placing one at the same path, and a policy test lists every override, so each one is a deliberate fork.

Options, flags and plugin points are added when a third site needs them, not before (the rule of three).

**Delivery (when the multi-site host exists).**
- Each host has its own image, Pulumi stacks and deploy job.
- CI builds and tests every host a change affects, and skips the rest.
- The standalone site keeps its own release gate. A shared change reaches it only through that gate.
- A patch to a shared library may deploy to the multi-site host automatically. A breaking change to a shared library updates every host in the same pull request.

**Triggers for adding the multi-site host.** Any of:
- a second site is commissioned that fits the shared document types;
- a site is planned that would otherwise copy this repository.

**Triggers for revisiting this decision.**
- Teams split, so that separate owners need separate release cadences: then consider shared packages (alternative 2).
- A site needs its own schema: then it gets a standalone host, not a place in the shared instance.

## Consequences

- Shared code now has an address, so review can tell a shared change from a site change, and the architecture rule of ADR 0009 stays a project boundary.
- With one site, the per-host seams have no second consumer yet. They are covered by unit tests, not by a second site. That is the deliberate limit: nothing more is added until a site needs it.
- The views stay in the Web project for now. Moving them to a Razor class library is part of adding the multi-site host, because only then do two hosts share them.
- The multi-site host needs, before it carries traffic:
  - more than one replica, which needs shared data protection keys and server roles (DEV-015);
  - back-office permissions per root node;
  - governance findings grouped by site (ADR 0018).
- CI that builds only affected hosts is not built yet. With one host, every change affects it.
