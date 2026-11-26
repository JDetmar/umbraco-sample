# Components

Pages are built from blocks in a Block List property called `blocks`, on both the `home` and `contentPage` document types. Each block type is a component. It has an element type in uSync, a view model, a partial view, documentation, fixtures and contract tests (ARC-01, ARC-02, TST-05).

| Component | Element type | Doc |
|---|---|---|
| Hero | `heroBlock` | [hero.md](hero.md) |
| Rich text | `richTextBlock` | [rich-text.md](rich-text.md) |
| Card grid | `cardGridBlock` (cards: `cardBlock`) | [card-grid.md](card-grid.md) |
| Call to action | `ctaBlock` | [call-to-action.md](call-to-action.md) |
| Form | not built yet | Waits for the Phase 4 threat model (ES-023) |

## How a block becomes HTML

1. `PageComponents.For(page)` reads the `blocks` property.
2. `ComponentMapper.Map` turns each block into a view model (`src/UmbracoSample.Components/ComponentModels.cs`). It reads values through `IBlockSource`, which `PublishedBlockSource` implements over Umbraco content.
3. `Views/Partials/Components/Page.cshtml` renders the page `h1`, then each model's partial view.

A block with missing required values maps to `null` and is skipped with a warning log naming the block key and type, never content. Required values are also mandatory in the back office, so this only catches content that predates a rule change, or a type without a renderer.

## Rules every component follows

- **One `h1` per page, and no skipped heading levels in component markup.** The page title is the only `h1`. Component headings are `h2`. Card titles are `h3` under a grid heading, or `h2` when the grid has none. The rich-text toolbar offers only `h2` and `h3`. The integration test `Every_page_has_one_h1_and_never_skips_a_heading_level` checks every fixture. Headings inside rich text are not yet enforced server-side (ledger DEV-007).
- **No image without alt text.** The `Image` media type has an `altText` property. `ImageModel.TryCreate` returns null when alt text is empty, so the image is not rendered. Alt text is not mandatory in the schema, because Umbraco's upload flow creates media before an editor can type. Decorative images belong in CSS.
- **Only safe links in link fields.** `LinkModel.TryCreate` keeps http, https, mailto and tel URLs and paths that stay on this site, and drops everything else (`javascript:`, `data:`, protocol-relative, and URLs containing backslashes, whitespace or control characters). Links inside rich text are not checked yet (ledger DEV-007). Links that open a new tab get `rel="noopener noreferrer"` and a visually hidden "(opens in a new tab)".
- **No layout or media in rich text, by editor configuration.** The `Rich text (components)` data type has no image, source-editing, style or class tools. That limits what the editor offers, not what can be stored: server-side sanitizing is ledger DEV-007, proposed for owner approval.
- **Text is encoded.** Every text field is HTML-encoded by Razor; `Text_fields_are_html_encoded` checks script and quotes in the maximal fixture.
- **Dropped values are logged.** A skipped block, an image without alt text and a rejected link each log a warning with keys and aliases only (ES-025).

## Fixtures

Synthetic seed content, imported only in Development and CI (`uSync/v17/Content/fixture-*` and `uSync/v17/Media/fixture-*`, ES-027). The project file keeps these fixture files out of the publish output, so they are not in the production image:

| Page | State |
|---|---|
| `/fixture-empty/` | No blocks |
| `/fixture-minimal/` | Every component with required fields only |
| `/fixture-maximal/` | Every component with every field, long text, new-tab links, a hero image with alt text, a hero whose image has none, and an encoding check |

The fixture images point at files that do not exist: uSync carries media metadata, not files, and the tests check markup, not pixels. These pages are the targets for the Phase 3 end-to-end, accessibility and visual-regression tests.

## Tests

- `tests/UmbracoSample.Web.Tests/Components`: contract tests for empty, minimal and maximal content per component, plus the link and image rules. They are fast and need no containers.
- `tests/UmbracoSample.Integration.Tests/ComponentFixtureTests.cs`: renders the fixtures through the real stack.

## Adding a component

1. In the local back office (`make run`), create the element type and add it to the `Page blocks` data type. uSync exports the files on save.
2. Add a view model and a mapping in `ComponentMapper`, then a partial view in `Views/Partials/Components`.
3. Add empty, minimal and maximal contract tests, and extend the fixture pages in the back office.
4. Write `docs/components/<name>.md`.
