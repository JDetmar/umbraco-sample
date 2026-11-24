# 0009. Redirect map as a parsed file, and a domain project for real logic

- Status: Proposed
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-002, ES-007, ES-018, ES-019, ES-020, ES-021; framework TST-08

## Context

A site migration moves pages. Missing or broken redirects cost search traffic without anyone noticing, so the framework requires that every legacy URL resolves to a live page (TST-08). Redirects are edited by hand, which makes them untrusted input (ES-021). Chains (A to B to C) and loops are common mistakes. This is the first real domain logic in the repository, and the plan keeps such logic in a project with no framework dependencies (ES-020) and an executable rule that keeps it that way (ES-018).

## Alternatives considered

- **Umbraco's built-in URL tracker.** It records redirects automatically when editors move content. It is good for in-site moves, and it stays on. But it does not cover URLs from a previous site, and its rules live in the database, where they cannot be reviewed or tested in CI.
- **A redirect package (for example Skybrud Redirects).** It gives editors a back-office UI, but it is a new dependency, the rules live in the database, and testing it means testing someone else's code.
- **Rewrite rules at the edge (Front Door).** These are fast, but they cannot be tested locally, and Front Door has rule-count limits.
- **ASP.NET Core rewrite middleware with an Apache or IIS rules file.** It is a regex language with no checks for chains, duplicates or unsafe targets.

## Decision

- **The map.** `src/UmbracoSample.Web/redirects.csv` (`source,target,kind`) is code, reviewed in pull requests.
- **The parser.** `src/UmbracoSample.Domain/Redirects` turns the file into precise types:
  - `SitePath` is normalised:
    - percent-decoded the way Kestrel decodes request paths (every escape except `%2F`);
    - lower case, with no trailing slash and no query;
    - no empty or dot segments, including dot segments written as escapes.
  - `RedirectTarget` is ASCII only, because Location is an HTTP header:
    - either a site path, optionally with a query and fragment;
    - or an absolute https URL without credentials.
    - The request query is inserted before any fragment.
  - `RedirectKind` is permanent (301) or temporary (302).
- **Rejected maps.** The parser rejects a map, with line numbers, if it has:
  - duplicate sources after normalisation;
  - self-redirects;
  - chains, which also rules out loops;
  - the home page or a reserved path (`/umbraco`, `/health`, `/dist`, `/media`, `/App_Plugins`) as a source.
- **A parsed map is flat.** Every visitor reaches the final page in one hop.
- **The site.** It parses the map before booting, and an invalid map stops startup. Middleware placed before Umbraco answers GET and HEAD for matching paths and keeps the request query unless the target has its own.
- **`UmbracoSample.Domain`** references no packages or projects. A NetArchTest rule fails the fast tests if it ever depends on Umbraco, ASP.NET Core, Microsoft.Extensions, EF Core, `System.Data`, `System.Net` or `System.IO`.
  - A committed negative test shows that the rule rejects a violating type.
  - A second test fails if the project file gains any reference.
- **Tests.**
  - CsCheck properties: parsing never throws; normalisation is idempotent; case and trailing slashes don't matter; every valid map finds every source; every parsed map is flat.
  - Example tests cover each rejection reason.
  - A retained corpus (`corpus/valid-*.csv`, `corpus/invalid-*.csv`) holds regression cases.
  - Middleware unit tests cover the behaviour.
  - An integration test follows every committed rule on the real stack and requires each on-site target to answer 200.

## Consequences

- **Adding a redirect is a one-line pull request.** CI rejects it if it chains, duplicates or targets something unsafe, and the integration test proves the target page exists.
- **Editors cannot add redirects without a developer.** That is intended for migration maps. In-site moves are still handled by Umbraco's URL tracker. If marketing needs self-service redirects, revisit this with a package and move the checks into a back-office validation step.
- **The committed rows are synthetic and point at seeded fixture pages.** A real migration replaces them. In production, on-site targets are checked by the integration test only against seed content, so the Phase 5 post-deployment check should follow the map against the deployed site.
- **Mutation testing runs weekly.** `make mutation` runs Stryker.NET on the Domain project, and the job fails below 75%. The first run scored 85.85%. Any input that ever breaks the parser is added to the corpus (see its README).
- **A large map (tens of thousands of rows) is still fine.** Lookup is a frozen dictionary, and parsing happens once at startup.
