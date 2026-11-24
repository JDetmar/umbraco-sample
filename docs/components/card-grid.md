# Card grid

A responsive grid of up to 12 cards. Element types `cardGridBlock` and `cardBlock`, view `Views/Partials/Components/CardGrid.cshtml`, model `CardGridModel`.

| Property | Alias | Type | Required | Notes |
|---|---|---|---|---|
| Heading | `heading` | Textstring | No | Rendered as `h2` |
| Cards | `cards` | Card list (1 to 12 `cardBlock`) | Yes | |

Each card:

| Property | Alias | Type | Required | Notes |
|---|---|---|---|---|
| Title | `title` | Textstring | Yes | `h3` under a grid heading, otherwise `h2` |
| Text | `text` | Textarea | No | |
| Link | `link` | Single link | No | Makes the title a link. The link text is always the title. |

**Not rendered when** no card has a title. Cards without a title are skipped. The list is a `ul`, so screen readers announce the number of cards.

**Fixtures:** the minimal fixture has one card and no heading. The maximal fixture has a heading and three cards: one with an internal link, one with a new-tab link, and one with no link.
