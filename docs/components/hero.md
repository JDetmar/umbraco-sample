# Hero

A page-opening banner. Element type `heroBlock`, view `Views/Partials/Components/Hero.cshtml`, model `HeroModel`.

| Property | Alias | Type | Required | Notes |
|---|---|---|---|---|
| Heading | `heading` | Textstring | Yes | Rendered as `h2` |
| Introduction | `intro` | Textarea | No | One or two sentences |
| Image | `image` | Image Media Picker (single) | No | Rendered only if the media item has alt text; requested at 1600 px wide |
| Link | `link` | Single link | No | Unsafe URLs are dropped |

**Not rendered when** the heading is empty.

**Fixtures:** the minimal fixture has the heading only. The maximal fixture adds a long heading, an introduction, an image with alt text and a new-tab link, followed by a second hero whose image has no alt text and so renders without it.

The image is not lazy-loaded, because a hero is usually above the fold.
