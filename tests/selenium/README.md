# SE3112 — Selenium UI / E2E testing

Owner: **IT24103271 — J M D K Jayasundara**

Tests operate on the real deployed React frontend and its ASP.NET microservices.
No Docker, local database, local backend or local frontend server is required.
The existing Sprint 1–2 script used obsolete selectors, hard-coded demo credentials,
and a remote Selenium server. This suite uses current page selectors, explicit
waits, assertions, fresh browser sessions, environment credentials, and JUnit reporting.

## Install once (Windows PowerShell)

Install Node.js 22+ and Google Chrome. Open PowerShell in the backend repository:

```powershell
cd tests/selenium
npm ci
npm run check
npm run test:public
```

Selenium Manager resolves ChromeDriver automatically; the first run needs internet
access and may take longer. See https://www.selenium.dev/documentation/selenium_manager/.
`npm run test:public` runs five public checks and explicitly SKIPS the customer journey.
Sorting is also explicitly skipped if there are fewer than two distinct product prices. It is not evidence that skipped coverage passed.

## Full customer journey

Use a dedicated **Customer** test account with an empty basket and a valid delivery address (saved address or complete account address). Have at least one
catalogue product with stock >= 2. Do not use your admin account or a teammate's
shopping account. The suite refuses an existing basket and only removes its own
single test product. Do not use the same test account concurrently.

```powershell
$env:APP_URL = 'https://gray-rock-0d7c57400.2.azurestaticapps.net'
$credential = Get-Credential -Message 'Enter the dedicated Customer test account'
$env:E2E_EMAIL = $credential.UserName
$env:E2E_PASSWORD = $credential.GetNetworkCredential().Password
npm test
# Browser-visible live demonstration:
npm run test:demo
# When finished:
Remove-Item Env:E2E_PASSWORD
Remove-Item Env:E2E_EMAIL
```

Full mode fails immediately when credentials are missing; it never silently skips
customer coverage. Do not commit credentials. On macOS/Linux export the same
variables and run `npm test`. Use `HEADLESS=false` for a visible Chrome window.
Optional `E2E_TIMEOUT_MS` adjusts explicit waits (default 20000).
`SELENIUM_REMOTE_URL` remains supported but is not required.

## Scenarios and scope

| ID | Scenario | Assertions |
|---|---|---|
| 01 | Real catalogue, search, no results, reset | Products loaded, every result matches, empty result shown, reset restores products |
| 02 | Price sorting | At least two products, all prices numeric and ascending |
| 06 | Quick View | Product name/price match, quantity lower bound, modal closes |
| 03 | Guest access protection | Cart and checkout redirect to login; checkout content absent |
| 04 | Invalid login | Identity rejection shown, login URL retained, no session token |
| 05 | Customer journey across Identity, Catalog and Order | Login, empty basket prerequisite, add product, quantity 1→2, subtotal/total, persistence after reload, checkout, missing-receipt guard, basket cleanup |

Scenario 05 checks the current business rule: no tax; total = subtotal + delivery fee.
The default run stops before order submission. It validates the connected
customer journey through checkout preparation, not successful order creation.
Authentication and product data are not mocked. Route protection is a UI check,
not a comprehensive API authorization security test.

### Optional order creation and history verification

Only for a team-approved disposable test account/environment. Prepare a saved test
delivery address, coordinate inventory/order handling with the team, and set:

```powershell
$env:E2E_ALLOW_ORDER = 'true'
npm run test:demo
Remove-Item Env:E2E_ALLOW_ORDER
```

This submits ONE cash-on-delivery order (quantity 2), captures its actual ID and
asserts that the SAME ID appears in order history. It can reserve inventory and
trigger normal order processing. It does not automatically cancel/delete the order;
record the printed order ID and reconcile it with the team. CI always leaves this
option disabled. No live order creation was performed during implementation.

## Reports and failure handling

- Console: named PASS/FAIL/SKIPPED scenarios and diagnostic messages.
- `reports/junit.xml`: machine-readable report for the group automation pipeline.
- CI automatically creates `reports/summary.md` and `summary.json` from the JUnit results; download the artifact for report evidence. Locally, if Python is installed, run `python summarize.py`.
- Reports are replaced on each run; save any evidence you need before rerunning.
- `reports/run-info.json`: timestamp, target, run scope and order-write flag.
- `reports/screenshots/`: failure screenshots where a browser session exists;
  login pages are excluded. These may contain test account details. Review before sharing.
- A failed assertion, startup failure or cleanup failure produces a nonzero exit code.
- Each scenario gets a fresh browser profile and closes it in `finally`.
- No fixed sleeps or automatic retries that could hide failures or duplicate orders.
- A browser/process crash can prevent cleanup. Inspect the dedicated account basket
  before rerunning; never clear a real customer's basket to make a test pass.

## GitHub Actions and group integration

The existing `.github/workflows/ci.yml` now calls the reusable E2E workflow after the backend test job on pull requests. This integrates backend tests/coverage and public UI checks in one workflow; other group roles still need their own integration.
It has a manual dispatch and a reusable `workflow_call` interface. It does not deploy
application code. Existing repository workflows still follow their own triggers.

After the workflow is merged, add repository Actions secrets `E2E_EMAIL` and
`E2E_PASSWORD` for the dedicated Customer account. In Actions → Selenium E2E → Run
workflow, leave `public_only` false for the full suite. Public mode explicitly reports
scenario 05 as skipped. Download `selenium-e2e-results` for JUnit and failure artifacts.
The job fails when the suite fails; the Actions summary lists each actual outcome.

To include it in the group's unified workflow after deployment/readiness:

```yaml
jobs:
  # Other group build/test jobs are defined above.
  e2e:
    needs: readiness # replace with your group's actual readiness job ID
    uses: ./.github/workflows/e2e.yml
    with:
      app_url: https://gray-rock-0d7c57400.2.azurestaticapps.net
      public_only: false
    secrets:
      E2E_EMAIL: ${{ secrets.E2E_EMAIL }}
      E2E_PASSWORD: ${{ secrets.E2E_PASSWORD }}
```

All suites should target a known deployed revision. Testing the deployed URL does
not prove that un-deployed changes on the PR branch work. Save the backend/frontend
commit SHAs and the actual deployment revision alongside final submission reports.

## Two-minute live demo

1. Explain the browser → Identity/Catalog/Order journey (15 seconds).
2. Show scenario 05's assertions and explicit waits (25 seconds).
3. Run `npm run test:demo` with the account ready, then explain output (60 seconds).
4. Show JUnit / Actions summary and explain a negative case and limitation (20 seconds).

Rehearse on the actual connection. A cold start can exceed the slot: show a recent
full run's report and run a selected workflow live if your lecturer permits it.
Screenshots alone do not replace live code.

## Submission checklist

- Name/ID and E2E ownership filled in; all four group roles documented in root README.
- Full run with real test credentials; failures fixed or clearly documented.
- Actual JUnit output and environment/revision recorded; no fabricated PASS claims.
- Group workflow invokes the E2E job; credentials in secrets, not source.
- Include source, tests, package lock, configurations and README in group submission.
- Exclude node_modules, secrets, personal account data and stale reports.

## Verified baseline and remaining work

The final October 18 CI run [39140194335](https://github.com/Online-Supermarket/Online-TNT-Supermarket-BE/actions/runs/39140194335) completed successfully. Public coverage previously showed 4 PASS and 2 SKIPPED: sorting lacked a second distinct price, and the authenticated customer journey lacked test credentials. Skipped scenarios must not be presented as passing.

The current update integrates E2E with backend CI and generates evidence summaries. Its new CI run is the authoritative result for this revision. Customer credentials, sufficient catalogue fixtures and a full successful run are still required before claiming complete coverage.

See [HANDOFF.md](HANDOFF.md) for the remaining user steps and report wording.
