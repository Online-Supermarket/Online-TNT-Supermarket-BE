# Sprint 1-2 JMeter scenario

With the disposable Compose stack running, execute:

```bash
docker run --rm -v "$PWD/tests/performance:/tests" justb4/jmeter:5.6.3 -n -t /tests/sprint-01-02.jmx -l /tests/results.jtl -e -o /tests/report
```

Archive `results.jtl` and the generated HTML `report` with the commit SHA, environment, database size and response-time targets. The plan runs public catalogue browsing and readiness checks. Authenticated checkout remains in the functional smoke suite because it needs per-user idempotency keys.
