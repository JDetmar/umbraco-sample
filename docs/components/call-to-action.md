# Call to action

A short prompt with one link. Element type `ctaBlock`, view `Views/Partials/Components/CallToAction.cshtml`, model `CtaModel`.

| Property | Alias | Type | Required | Notes |
|---|---|---|---|---|
| Heading | `heading` | Textstring | Yes | Rendered as `h2` |
| Text | `text` | Textarea | No | |
| Link | `link` | Single link | Yes | Unsafe URLs are dropped, which also stops the block from rendering |

**Not rendered when** the heading is empty or there is no safe link.

**Fixtures:** the minimal fixture has a heading and a relative link. The maximal fixture adds supporting text and an absolute link.
