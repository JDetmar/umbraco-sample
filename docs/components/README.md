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
2. `ComponentMapper.Map` turns each block into a view model (`Components/ComponentModels.cs`). It reads values through `IBlockSource`, which `PublishedBlockSource` implements over Umbraco content.
3. `Views/Partials/Components/Page.cshtml` renders the page `h1`, then each model's partial view.

A block with missing required values maps to `null` and is skipped with a warning log naming the block key and type, never content. Required values are also mandatory in the back office, so this only catches content that predates a rule change, or a type without a renderer.

## Rules every component follows

- **One `h1` per page, and no skipped heading levels.** The page title is the only `h1`. Component headings are `h2`. Card titles are `h3` under a grid heading, or `h2` when the grid has none. Rich text offers only `h2` and `h3`. The integration test `Every_page_has_one_h1_and_never_skips_a_heading_level` checks every fixture.
- **No image without alt text.** The `Image` media type has an `altText` property. `ImageModel.TryCreate` returns null when alt text is empty, so the image is not rendered. Alt text is not mandatory in the schema, because Umbraco's upload flow creates media before an editor can type. Decorative images belong in CSS.
- **Only safe links.** `LinkModel.TryCreate` keeps http, https, mailto and tel URLs and site-relative paths, and drops everything else (`javascript:`, `data:`, protocol-relative). Links that open a new tab get `rel="noopener noreferrer"` and a visually hidden "(opens in a new tab)".
- **No layout or media in rich text.** The `Rich text (components)` data type has no image, source-editing, style or class tools. Layout comes from components.

## Fixtures

Synthetic seed content, imported only in Development and CI (`uSync/v17/Content`, ES-027):

| Page | State |
|---|---|
| `/fixture-empty/` | No blocks |
| `/fixture-minimal/` | Every component with required fields only |
| `/fixture-maximal/` | Every component with every field, long text and new-tab links |

These pages are the targets for the Phase 3 end-to-end, accessibility and visual-regression tests.

## Tests

- `tests/UmbracoSample.Web.Tests/Components`: contract tests for empty, minimal and maximal content per component, plus the link and image rules. They are fast and need no containers.
- `tests/UmbracoSample.Integration.Tests/ComponentFixtureTests.cs`: renders the fixtures through the real stack.

## Adding a component

1. In the local back office (`make run`), create the element type and add it to the `Page blocks` data type. uSync exports the files on save.
2. Add a view model and a mapping in `ComponentMapper`, then a partial view in `Views/Partials/Components`.
3. Add empty, minimal and maximal contract tests, and extend the fixture pages in the back office.
4. Write `docs/components/<name>.md`.
