# Engineering Rigor Framework

Platform-agnostic engineering requirements for a component-based CMS website, and the reference this sample repository implements on Umbraco.

Requirements carry an ID, a priority (Must, Should, Could) and an owner.

## Principles

1. **Platform-agnostic rigor.** Every requirement here applies to Umbraco or any other CMS.
2. **Engineering owns the guardrails.** Software Development owns the repo, pipeline, environments and standards; every contributor builds inside them.
3. **The site is a product, not a project.** It has a durable team, a roadmap and outcome metrics.
4. **The component model bridges marketing and engineering.** Engineering certifies the parts; editors compose pages from them without code changes.

## Ownership and operating model

Software Development owns the mechanisms; Marketing and Legal/Privacy each own a defined slice.

| Party | Owns |
|---|---|
| Software Development | Repo, cloud environments, CI/CD pipeline, engineering standards, code review, component certification, monitoring, compliance automation |
| Website product owner (Software Development) | Roadmap, backlog, delivery, outcome metrics |
| Marketing counterpart | Business goals, priority sign-off, page composition, content quality and content-level compliance (alt text, plain language) |
| Legal / Privacy office | Privacy and accessibility policy, approval of legal text, disclosures and new data-collecting forms |

| ID | Requirement | Priority |
|---|---|---|
| OWN-01 | Software Development owns the source repository, cloud environments and deployment pipeline; no contributor has direct production access | Must |
| OWN-02 | All work arrives as pull requests and merges only after Software Development review | Must |
| OWN-03 | A named website product owner from Software Development, paired with a named marketing counterpart, shares accountability for outcome metrics | Must |
| OWN-04 | Legal/Privacy formally owns policy and approval of regulated content | Must |
| OWN-05 | The site runs as a durable product team with a standing roadmap | Should |

## Architecture and component model

The site is rebuilt around a tested, versioned component library that marketing composes pages from.

| ID | Requirement | Priority | Owner |
|---|---|---|---|
| ARC-01 | Pages are built from a library of reusable components (hero, cards, CTAs, form blocks), not one-off templates. In Umbraco this uses Block Grid / Block List | Must | Software Development |
| ARC-02 | Each component is versioned, documented and tested once, then reused everywhere | Must | Software Development |
| ARC-03 | The content model (content types, blocks, data types) lives in source control. Umbraco: uSync; code-first CMS: schema in code | Must | Software Development |
| ARC-04 | Schema and code flow up from dev through Git; content is authored only in production | Must | Software Development |
| ARC-05 | Design tokens (color, type, spacing) are synced from Figma into code so design and build can't drift | Should | Software Development + Design |
| ARC-06 | Forms are built natively in the site and post to the marketing automation platform (MAP) through its API; the marketing automation platform stays the automation engine, keeping a deliberate separation that limits lock-in | Should | Software Development + Marketing |
| ARC-07 | Components include analytics and A/B test hooks by default | Could | Software Development |
| ARC-08 | Pages expose clean structured data so AI search engines can read them | Could | Software Development |

## Environments and delivery

Every change moves through automated, repeatable environments, and any deployment can be rolled back.

| ID | Requirement | Priority |
|---|---|---|
| ENV-01 | All Azure resources are defined as infrastructure as code; no manual changes to environments | Must |
| ENV-02 | Dev, staging and production environments with a defined promotion path | Must |
| ENV-03 | Every deployment is automated through the pipeline and can be rolled back | Must |
| ENV-04 | Every pull request gets its own preview environment for marketing and QA review before merge | Should |
| ENV-05 | Feature flags let code ship dark and release on marketing's schedule | Should |
| ENV-06 | Schema drift between Git and any environment fails the build (Umbraco: uSync drift detection) | Must |

## CI/CD quality gates and testing

Automated gates replace manual testing as the source of confidence; nothing reaches production without passing them. Marketing-automation form contract tests and redirect tests are the top priorities, since those failures cost leads and search traffic silently.

| ID | Stage | Check | Priority |
|---|---|---|---|
| TST-01 | Every commit | Linting, formatting and type checks (C# and front end) | Must |
| TST-02 | Every commit | Unit tests with a minimum coverage bar on new code | Must |
| TST-03 | Every commit | Bundle size and image optimization limits | Should |
| TST-04 | Every commit | Dependency, static code and secret scanning | Must |
| TST-05 | Platform | Content model contract tests: every component renders with empty, minimal and maximal content | Must |
| TST-06 | Platform | Schema drift detection (see ENV-06) | Must |
| TST-07 | Integration | Marketing automation form contract tests against a sandbox | Must |
| TST-08 | Integration | Redirect map tests: every legacy URL resolves to a live page | Must |
| TST-09 | Integration | Security headers and content security policy checks | Must |
| TST-10 | Integration | Third-party script and tracker allowlist; unapproved tags fail the build | Must |
| TST-11 | Quality | Visual regression on component fixtures and a sample of real pages | Must |
| TST-12 | Quality | End-to-end tests on critical paths: navigation, search, forms | Must |
| TST-13 | Quality | Automated accessibility scans (e.g. axe) against WCAG 2.2 AA | Must |
| TST-14 | Quality | Performance budgets and Core Web Vitals thresholds | Should |
| TST-15 | Quality | SEO checks: broken links, metadata, canonical tags, structured data | Must |
| TST-16 | Post-deploy | Smoke tests in production with automatic rollback on failure | Must |
| TST-17 | Post-deploy | Cross-browser and mobile visual checks on a rotating page sample | Should |

## Accessibility, privacy and legal compliance

Website accessibility lawsuits are common, so compliance needs a clear owner. It is enforced at four points: the components, the pipeline, the content workflow and the live site. Automated scans catch roughly a third to half of accessibility issues, so manual review stays in the plan.

| ID | Where | Requirement | Priority | Owner |
|---|---|---|---|---|
| CMP-01 | Components | Every component meets WCAG 2.2 AA: keyboard navigation, contrast, required alt text fields, correct heading structure | Must | Software Development |
| CMP-02 | Pipeline | Accessibility and tracker allowlist gates on every pull request (TST-10, TST-13) | Must | Software Development |
| CMP-03 | Content workflow | Changes to privacy policy, disclosures, legal text and new forms collecting personal data require Legal/Privacy approval before publishing | Must | Legal/Privacy |
| CMP-04 | Content workflow | Authors are guided to supply alt text and plain language at publish time | Should | Marketing |
| CMP-05 | Live site | Scheduled scans for accessibility, cookies and trackers | Must | Software Development |
| CMP-06 | Live site | Verification that the consent banner blocks tags until opt-in (applicable privacy law) | Must | Software Development + Legal/Privacy |
| CMP-07 | Periodic | Manual accessibility audit with assistive technology, at least yearly | Should | Software Development |

## Security and operations

The live site is protected, monitored against defined service levels, and provably recoverable.

| ID | Requirement | Priority |
|---|---|---|
| OPS-01 | Web application firewall in front of the site | Must |
| OPS-02 | Third-party penetration test at least yearly and before major launches | Must |
| OPS-03 | Real user monitoring, uptime checks and alerting | Must |
| OPS-04 | Defined SLOs for availability and page speed, reviewed monthly | Should |
| OPS-05 | Backups with restores tested on a schedule, not just taken | Must |
| OPS-06 | Incident runbooks and an on-call path for the site | Should |

## Governance and content lifecycle

Every page has an owner and an expiry date, so content never becomes stale or orphaned.

| ID | Requirement | Priority | Owner |
|---|---|---|---|
| GOV-02 | Every page has a named content owner and a review or expiry date | Must | Marketing |
| GOV-03 | Expired or ownerless pages are flagged automatically | Should | Software Development |
| GOV-04 | Architecture decision records capture major choices (platform, hosting, integrations) with their reasoning | Must | Software Development |
| GOV-05 | Coding standards and a definition of done are documented and enforced in review | Must | Software Development |
| GOV-06 | A defined split of what belongs on the main site versus a separate no-code tool for campaigns and microsites, so speed-first work stays off the engineered platform | Should | Software Development + Marketing |

## Measurement, experimentation and AI-assisted workflow

The team measures delivery and business outcomes, tests decisions instead of debating them, and uses AI to scale review and quality.

| ID | Requirement | Priority |
|---|---|---|
| MSR-01 | DORA metrics tracked: deployment frequency, lead time, change failure rate, time to restore | Must |
| MSR-02 | Business outcome metrics owned jointly by product owner and marketing: lead conversions, form completion, organic search traffic | Must |
| MSR-03 | A/B testing capability available to marketing through components | Could |
| AI-01 | AI review of every pull request against the engineering standards, alongside human review | Should |
| AI-02 | AI-assisted test generation for new components | Should |
| AI-03 | AI checks on content before publish: accessibility, tone, broken or outdated claims | Could |
| AI-05 | Site readable by AI search engines (see ARC-08) | Could |
