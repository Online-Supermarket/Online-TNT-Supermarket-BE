# E2E handoff — SE3112

## Implemented work

- Docker-free Selenium/Chrome runner, six named scenarios, explicit waits and assertions.
- Separate public and authenticated modes; missing credentials fail full mode.
- Real Identity/Catalog/Order interactions; isolated browser profiles; test basket cleanup.
- JUnit results, failure screenshots, automatic Markdown/JSON evidence summaries in CI.
- Backend CI invokes public E2E after backend tests. Reusable/manual workflow supports full mode.
- PowerShell setup, optional order creation, demo and report guidance.

## What the student must supply

1. A dedicated Customer account with empty basket and valid delivery address.
2. Two catalogue products with distinct prices; at least one with stock >= 2.
3. Repository Actions secrets E2E_EMAIL and E2E_PASSWORD, entered privately by a repository administrator, or equivalent local environment variables.
4. Actual student name/ID in README; review the implemented code and understand it.

After the workflow reaches the default branch through the team's reviewed merge,
run Actions → Selenium E2E with public_only=false. Before merging, run the full
suite locally on testing/e2e as shown in README. Do not merge solely to hide failed
or skipped tests. A merge to main may trigger existing application deployment workflows.

## Evidence for the report

Download selenium-e2e-results from the relevant Actions run. Use summary.md,
summary.json and junit.xml. Record the exact run URL and date. The summary reports
skips separately and does not invent a deployed revision. A green public job is not
proof that authenticated checkout passed. Failure screenshots are diagnostic evidence;
manual screenshots are optional unless the lecturer separately requests them.

Suggested implementation description (verify the current run before adding results):

"The UI/E2E suite uses Selenium WebDriver with JavaScript to exercise the deployed
Marketflow frontend and its real microservices. It checks catalogue search, product
details, guest access protection and invalid authentication. The authenticated
scenario checks customer login, basket updates, totals, persistence after refresh,
and checkout payment validation. Explicit waits synchronize with UI state. The
pipeline invokes the suite and publishes JUnit results and an execution summary.
Tests with unavailable prerequisites are identified explicitly; they are not counted
as passes. Normal runs do not submit orders. An optional mode verifies order creation
and the matching order-history entry using controlled test data."

Add a results table using actual summary.md values, and list any skipped coverage
under limitations. Describe AI assistance according to your course's rules; do not
label automatically generated evidence as manually performed testing.

## Two-minute walkthrough

- 0:00–0:20: Explain the real browser → Identity/Catalog/Order flow.
- 0:20–0:45: Show assertions and explicit waits in e2e.test.mjs.
- 0:45–1:40: Run the prepared suite or a lecturer-approved selected live scenario.
- 1:40–2:00: Explain actual results, skipped tests and why default runs stop before order submission.

Viva concepts: E2E vs unit testing; assertion vs click; explicit waits vs fixed
sleep; headless vs visible browser; fresh sessions; reproducible test data; why
skipped differs from passed; and why a live deployed test does not validate
un-deployed source changes. Rehearse the runtime before the viva.
