const { Client } = require('pg');
const client = new Client({
  user: 'marketflow',
  password: 'marketflow',
  host: 'localhost',
  database: 'marketflow'
});
client.connect().then(() => {
  client.query("SELECT p.payment_method FROM ordering.orders o LEFT JOIN ordering.order_payments p ON p.order_id=o.id WHERE o.id = 'a34980c4-5b25-4c15-966a-ef2395c3a78a'").then(res => {
    console.log(JSON.stringify(res.rows));
    client.end();
  });
});
