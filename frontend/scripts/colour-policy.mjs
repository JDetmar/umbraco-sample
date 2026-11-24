// Colour policy (ARC-05): outside the token definitions, every colour-bearing declaration uses tokens or a CSS keyword.
// Checked on the built stylesheet, so every source file and every future file is covered.

const colourProperty = /^(color|background(-color)?|border(-(top|right|bottom|left|block|inline|block-start|block-end|inline-start|inline-end))?(-color)?|outline(-color)?|fill|stroke|box-shadow|text-shadow|text-decoration(-color)?|caret-color|accent-color|column-rule(-color)?|background-image|border-image(-source)?|--[a-z0-9-]+)$/i;

// Words that may appear in a colour-bearing value without being a colour.
const allowedWords = new Set([
  'currentcolor', 'transparent', 'inherit', 'initial', 'unset', 'revert', 'none',
  'solid', 'dashed', 'dotted', 'double', 'groove', 'ridge', 'inset', 'outset', 'hidden',
  'underline', 'overline', 'line-through', 'auto', 'thin', 'medium', 'thick',
  'linear-gradient', 'radial-gradient', 'conic-gradient', 'repeating-linear-gradient', 'repeating-radial-gradient',
  'to', 'top', 'right', 'bottom', 'left', 'deg', 'circle', 'ellipse', 'at', 'center',
]);

/** Removes the first :root block, the one Style Dictionary generates from the tokens. Any other :root is checked. */
function withoutTokenBlock(css) {
  return css.replace(/:root\s*\{[^}]*\}/, '');
}

/** Returns "property: value" for every declaration that sets a colour without a token. */
export function rawColourDeclarations(css) {
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '');
  // The generated :root block is where tokens are defined; everything else, including custom properties, must use them.
  const withoutRoot = withoutTokenBlock(withoutComments);
  const violations = [];

  for (const match of withoutRoot.matchAll(/(--?[a-z][a-z0-9-]*)\s*:\s*([^;{}]+)/gi)) {
    const [, property, value] = match;
    if (!colourProperty.test(property)) continue;

    const remainder = value
      .replace(/var\(--[a-z0-9-]+\)/gi, ' ')
      .replace(/!important/gi, ' ')
      .replace(/[()]/g, ' ')
      .replace(/-?\d*\.?\d+(px|rem|em|%|ch|vw|vh)?/gi, ' ')
      .split(/[\s,]+/)
      .filter((word) => word && !allowedWords.has(word.toLowerCase()));

    if (remainder.length > 0) violations.push(`${property}: ${value.trim()}`);
  }

  return violations;
}

/** Every colour token referenced by a color or background declaration in the stylesheet. */
export function coloursInUse(css) {
  const withoutRoot = withoutTokenBlock(css);
  const used = new Set();
  for (const match of withoutRoot.matchAll(/(?:^|[;{\s])(color|background(?:-color)?)\s*:\s*([^;{}]+)/gi)) {
    for (const token of match[2].matchAll(/var\(--(color-[a-z0-9-]+)\)/gi)) used.add(token[1]);
  }
  return used;
}
