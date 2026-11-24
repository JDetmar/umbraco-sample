# Rich text

Formatted editor text. Element type `richTextBlock`, view `Views/Partials/Components/RichText.cshtml`, model `RichTextModel`.

| Property | Alias | Type | Required | Notes |
|---|---|---|---|---|
| Text | `text` | Rich text (components) | Yes | Headings `h2` and `h3`, bold, italic, lists, quotes, links |

The data type leaves out images, source editing, inline styles and classes, so editors cannot break layout or add images without alt text.

**Not rendered when** the text is empty or only whitespace.

**Fixtures:** the minimal fixture has one paragraph. The maximal fixture has `h2`, `h3`, emphasis, a link, both list types and a quotation.
