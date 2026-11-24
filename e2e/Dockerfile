# The pinned Playwright image that scripts/e2e.sh runs the tests in. It is not built; the script reads this FROM line.
# It lives here so Dependabot (docker ecosystem, /e2e) proposes digest updates, and a policy test keeps the
# tag equal to the @playwright/test version in package.json, because the browsers must match the library.
FROM mcr.microsoft.com/playwright:v1.65.1-noble@sha256:1507cbfc550a63a6b7396791ca4f00df17beb56342d2447b3f106c306f525579
