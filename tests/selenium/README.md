# Sprint 1-2 Selenium scenario

Start the backend Compose stack and the frontend (`npm run dev`) first. Then run:

```bash
docker run --rm -d --name marketflow-selenium -p 4444:4444 selenium/standalone-chrome:4.37.0
cd tests/selenium && npm install && APP_URL=http://host.docker.internal:5173 npm test
docker stop marketflow-selenium
```

The scenario checks that the browser can load the storefront, authenticate the demo customer and navigate to the basket. Extend it with product creation and checkout only against a disposable database.
