// Pages under test: the synthetic seed content (ES-027). Fixture pages exist only in Development and CI.
export const pages = [
  { name: 'home', path: '/' },
  { name: 'fixture-empty', path: '/fixture-empty/' },
  { name: 'fixture-minimal', path: '/fixture-minimal/' },
  { name: 'fixture-maximal', path: '/fixture-maximal/' },
];

// The lead form's fixture. Locally and in CI the form is on and posts to the in-memory MAP stub.
export const formPage = { name: 'fixture-form', path: '/fixture-form/' };
pages.push(formPage);
