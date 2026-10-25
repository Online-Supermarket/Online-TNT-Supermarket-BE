"""Build report evidence from actual JUnit output; never infer passes from job status."""
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET


def summarize(folder=Path('reports')):
    metadata = json.loads((folder / 'run-info.json').read_text()) if (folder / 'run-info.json').exists() else {}
    junit = folder / 'junit.xml'
    rows = []
    error = ''
    if junit.exists():
        try:
            for case in ET.parse(junit).getroot().iter('testcase'):
                failure = case.find('failure') is not None or case.find('error') is not None
                skipped = case.find('skipped')
                status = 'FAIL' if failure else 'SKIPPED' if skipped is not None else 'PASS'
                reason = skipped.get('message', '') if skipped is not None else ''
                rows.append((case.get('name', ''), status, reason))
        except ET.ParseError:
            error = 'JUnit output is incomplete or invalid. This run is not verified.'
    else:
        error = 'No JUnit output was produced. Check setup or missing credentials; no tests are claimed to pass.'
    counts = {s: sum(row[1] == s for row in rows) for s in ['PASS', 'FAIL', 'SKIPPED']}
    summary = {'counts': counts, 'scope': metadata.get('scope', 'unknown'), 'error': error,
               'complete': bool(rows) and not error and counts['FAIL'] == 0 and counts['SKIPPED'] == 0,
               'tests': [{'name': n, 'status': s, 'skip_reason': r} for n, s, r in rows]}
    def safe(value):
        return str(value).replace('|', '/').replace('\n', ' ').replace('\r', ' ')
    lines = ['# Marketflow E2E execution evidence', '',
             f"- Started (UTC): {safe(metadata.get('started', 'unknown'))}",
             f"- Target: {safe(metadata.get('url', 'unknown'))}",
             f"- Scope: {safe(metadata.get('scope', 'unknown'))}",
             f"- Test code revision: {safe(os.getenv('GITHUB_SHA', 'local run'))}",
             '- Deployed application revision: not established by this run',
             f"- Passed: {counts['PASS']}; failed: {counts['FAIL']}; skipped: {counts['SKIPPED']}",
             '- Order submission: ' + ('enabled' if metadata.get('createsOrder') else 'disabled'), '']
    if os.getenv('GITHUB_RUN_ID'):
        lines += [f"- CI run: https://github.com/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}", '']
    lines += [error or ('All selected scenarios executed without failures or skips.' if summary['complete'] else 'Coverage is incomplete: skipped tests are not passing evidence.'), '',
              '| Scenario | Result | Skip reason |', '|---|---|---|']
    lines += [f'| {safe(n)} | {s} | {safe(r)} |' for n, s, r in rows]
    lines += ['', 'Default customer coverage ends at checkout validation. Successful order creation/history is only covered when explicitly enabled and executed.',
              'These are automated test results, not evidence of manual testing. Retain the original JUnit file with this summary.', '']
    folder.mkdir(parents=True, exist_ok=True)
    markdown = '\n'.join(lines)
    (folder / 'summary.md').write_text(markdown, encoding='utf-8')
    (folder / 'summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    return markdown

if __name__ == '__main__':
    result = summarize()
    if os.getenv('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as output:
            output.write(result)
    print(result)
