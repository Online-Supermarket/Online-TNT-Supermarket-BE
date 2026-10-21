const http = require('http');
const req = http.request({
  hostname: 'localhost',
  port: 8080,
  path: '/order/reports/sales',
  method: 'GET',
  headers: { 'Authorization': 'Bearer test' } // This will fail but maybe give a clue, or wait I can just write a C# console app to test it. Or just use psql?
});
