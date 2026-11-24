// Design tokens (ARC-05): JSON in tokens/ is the source; CSS custom properties are generated into build/.
export default {
  source: ['tokens/**/*.json'],
  platforms: {
    css: {
      transformGroup: 'css',
      buildPath: 'build/',
      files: [
        {
          destination: 'tokens.css',
          format: 'css/variables',
          options: { outputReferences: true },
        },
      ],
    },
    json: {
      transformGroup: 'js',
      buildPath: 'build/',
      files: [{ destination: 'tokens.json', format: 'json/flat' }],
    },
  },
};
