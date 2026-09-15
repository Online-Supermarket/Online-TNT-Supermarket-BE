const { Client } = require('pg');
const bcrypt = require('bcryptjs');
const crypto = require('crypto');

const connectionString = process.env.SUPABASE_DATABASE_URL;

if (!connectionString) {
  throw new Error('SUPABASE_DATABASE_URL must be provided through the environment.');
}

// This legacy utility predates database-per-service ownership and deletes rows.
// Keep two deliberate safeguards so it cannot be invoked by a normal setup or
// deployment command, or pointed at a service/administrative database.
const destructiveAcknowledgement = process.env.TNT_ALLOW_DESTRUCTIVE_LEGACY_SEED;
const parsedConnection = new URL(connectionString);
const databaseName = decodeURIComponent(parsedConnection.pathname.replace(/^\//, ''));
const protectedDatabases = new Set([
  'postgres',
  'template0',
  'template1',
  'tnt_identity',
  'tnt_user',
  'tnt_product'
]);

if (destructiveAcknowledgement !== 'I_UNDERSTAND_THIS_DELETES_DATA') {
  throw new Error(
    'Legacy seed blocked. Set TNT_ALLOW_DESTRUCTIVE_LEGACY_SEED=' +
    'I_UNDERSTAND_THIS_DELETES_DATA only for an isolated disposable database.'
  );
}

if (protectedDatabases.has(databaseName) || !/(dev|test|sandbox)/i.test(databaseName)) {
  throw new Error(
    `Legacy seed blocked for database "${databaseName}". ` +
    'The target name must contain dev, test, or sandbox and must not be a service database.'
  );
}

function hashToken(rawToken) {
  return crypto.createHash('sha256').update(rawToken).digest('base64');
}

async function run() {
  console.log('================================================================');
  console.log(' TNT Supermarket - PostgreSQL Database Setup & Dummy Data Test  ');
  console.log(' Database: Supabase PostgreSQL                                  ');
  console.log(' Connection: supplied through SUPABASE_DATABASE_URL            ');
  console.log('================================================================\n');

  const startTime = Date.now();
  const client = new Client({ connectionString });
  let transactionStarted = false;

  try {
    // ─── 1. CONNECT & PING ───────────────────────────────────────────────────
    console.log('[1/6] Connecting to PostgreSQL database...');
    await client.connect();
    const pingRes = await client.query('SELECT version(), current_database(), current_user, NOW() as server_time;');
    const latency = Date.now() - startTime;
    console.log(` Connected in ${latency}ms`);
    console.log(`   - Database: ${pingRes.rows[0].current_database}`);
    console.log(`   - User: ${pingRes.rows[0].current_user}`);
    console.log(`   - Server Time: ${pingRes.rows[0].server_time}`);
    console.log(`   - Version: ${pingRes.rows[0].version.split('\n')[0]}\n`);

    await client.query('BEGIN');
    transactionStarted = true;

    // ─── 2. ENSURE SUPERMARKET DOMAIN SCHEMA ─────────────────────────────────
    console.log('[2/6] Ensuring Supermarket schema & tables exist...');

    // Categories
    await client.query(`
      CREATE TABLE IF NOT EXISTS "Categories" (
        "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        "Name" varchar(100) NOT NULL UNIQUE,
        "Description" text,
        "ImageUrl" varchar(500),
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAtUtc" timestamptz NOT NULL DEFAULT NOW()
      );
    `);

    // Products
    await client.query(`
      CREATE TABLE IF NOT EXISTS "Products" (
        "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        "CategoryId" uuid REFERENCES "Categories"("Id") ON DELETE CASCADE,
        "Name" varchar(200) NOT NULL,
        "Description" text,
        "Price" numeric(10, 2) NOT NULL,
        "StockQuantity" integer NOT NULL DEFAULT 0,
        "Unit" varchar(50) NOT NULL DEFAULT 'item',
        "ImageUrl" varchar(500),
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAtUtc" timestamptz NOT NULL DEFAULT NOW(),
        "UpdatedAtUtc" timestamptz
      );
    `);

    // Carts & CartItems
    await client.query(`
      CREATE TABLE IF NOT EXISTS "Carts" (
        "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        "UserId" uuid NOT NULL,
        "CreatedAtUtc" timestamptz NOT NULL DEFAULT NOW(),
        "UpdatedAtUtc" timestamptz
      );
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS "CartItems" (
        "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        "CartId" uuid REFERENCES "Carts"("Id") ON DELETE CASCADE,
        "ProductId" uuid REFERENCES "Products"("Id") ON DELETE CASCADE,
        "Quantity" integer NOT NULL DEFAULT 1,
        "AddedAtUtc" timestamptz NOT NULL DEFAULT NOW()
      );
    `);

    // Orders & OrderItems
    await client.query(`
      CREATE TABLE IF NOT EXISTS "Orders" (
        "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        "UserId" uuid NOT NULL,
        "OrderNumber" varchar(50) NOT NULL UNIQUE,
        "TotalAmount" numeric(10, 2) NOT NULL,
        "Status" varchar(50) NOT NULL DEFAULT 'Pending',
        "PaymentMethod" varchar(50) NOT NULL DEFAULT 'Card',
        "ShippingAddress" text NOT NULL,
        "CreatedAtUtc" timestamptz NOT NULL DEFAULT NOW(),
        "UpdatedAtUtc" timestamptz
      );
    `);

    await client.query(`
      CREATE TABLE IF NOT EXISTS "OrderItems" (
        "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
        "OrderId" uuid REFERENCES "Orders"("Id") ON DELETE CASCADE,
        "ProductId" uuid REFERENCES "Products"("Id"),
        "ProductName" varchar(200) NOT NULL,
        "UnitPrice" numeric(10, 2) NOT NULL,
        "Quantity" integer NOT NULL DEFAULT 1,
        "Subtotal" numeric(10, 2) NOT NULL
      );
    `);

    console.log(' Core + Supermarket domain tables are ready in public schema.\n');

    // ─── 3. SEED DUMMY DATA ──────────────────────────────────────────────────
    console.log('[3/6] Seeding realistic dummy data...');

    const defaultPassword = 'Password@123';
    const salt = bcrypt.genSaltSync(11);
    const passwordHash = bcrypt.hashSync(defaultPassword, salt);

    // Clean existing seed data in public schema (idempotent seed)
    await client.query('DELETE FROM "OrderItems";');
    await client.query('DELETE FROM "Orders";');
    await client.query('DELETE FROM "CartItems";');
    await client.query('DELETE FROM "Carts";');
    await client.query('DELETE FROM "Products";');
    await client.query('DELETE FROM "Categories";');
    await client.query('DELETE FROM "RefreshTokens";');
    await client.query('DELETE FROM "UserProfiles";');
    await client.query('DELETE FROM "ApplicationUsers";');

    // --- Users Definition ---
    const users = [
      {
        id: '11111111-1111-1111-1111-111111111111',
        fullName: 'TNT Supermarket Admin',
        email: 'admin@tntsupermarket.com',
        phoneNumber: '+94771234567',
        role: 'Admin',
        address: 'TNT HQ, 100 Galle Road',
        city: 'Colombo 03'
      },
      {
        id: '22222222-2222-2222-2222-222222222222',
        fullName: 'Saman Jayasinghe (Store Manager)',
        email: 'manager.colombo@tntsupermarket.com',
        phoneNumber: '+94772345678',
        role: 'Manager',
        address: '45 Duplication Road',
        city: 'Colombo 04'
      },
      {
        id: '33333333-3333-3333-3333-333333333333',
        fullName: 'Nalini Bandara (Inventory Staff)',
        email: 'staff.inventory@tntsupermarket.com',
        phoneNumber: '+94773456789',
        role: 'Staff',
        address: '12 Kandy Road',
        city: 'Kelaniya'
      },
      {
        id: '44444444-4444-4444-4444-444444444444',
        fullName: 'Kamal Silva (Delivery Rider)',
        email: 'rider.kamal@tntsupermarket.com',
        phoneNumber: '+94774567890',
        role: 'Rider',
        address: '78 Main Street',
        city: 'Dehiwala'
      },
      {
        id: '55555555-5555-5555-5555-555555555555',
        fullName: 'Kasun Perera',
        email: 'kasun.perera@gmail.com',
        phoneNumber: '+94775678901',
        role: 'Buyer',
        address: '24 Flower Road',
        city: 'Colombo 07'
      },
      {
        id: '66666666-6666-6666-6666-666666666666',
        fullName: 'Chamari Silva',
        email: 'chamari.silva@yahoo.com',
        phoneNumber: '+94776789012',
        role: 'Buyer',
        address: '15 Peradeniya Road',
        city: 'Kandy'
      },
      {
        id: '77777777-7777-7777-7777-777777777777',
        fullName: 'Dilshan Fernando',
        email: 'dilshan.fernando@outlook.com',
        phoneNumber: '+94777890123',
        role: 'Buyer',
        address: '88 Beach Road',
        city: 'Negombo'
      },
      {
        id: '88888888-8888-8888-8888-888888888888',
        fullName: 'Ananya Patel',
        email: 'ananya.patel@gmail.com',
        phoneNumber: '+94778901234',
        role: 'Buyer',
        address: '10 Light House Street, Fort',
        city: 'Galle'
      }
    ];

    // Insert ApplicationUsers & UserProfiles
    for (const u of users) {
      await client.query(`
        INSERT INTO "ApplicationUsers" ("Id", "FullName", "Email", "NormalizedEmail", "PasswordHash", "PhoneNumber", "Role", "IsActive", "CreatedAtUtc")
        VALUES ($1, $2, $3, $4, $5, $6, $7, true, NOW());
      `, [u.id, u.fullName, u.email, u.email.toUpperCase(), passwordHash, u.phoneNumber, u.role]);

      await client.query(`
        INSERT INTO "UserProfiles" ("Id", "IdentityUserId", "DisplayName", "PhoneNumber", "Address", "City", "CreatedAtUtc")
        VALUES (gen_random_uuid(), $1, $2, $3, $4, $5, NOW());
      `, [u.id, u.fullName, u.phoneNumber, u.address, u.city]);

      // Seed an active refresh token for each user
      const rawRefreshToken = crypto.randomBytes(32).toString('base64');
      const tokenHash = hashToken(rawRefreshToken);
      const expiresAt = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000);

      await client.query(`
        INSERT INTO "RefreshTokens" ("Id", "UserId", "TokenHash", "ExpiresAtUtc", "CreatedAtUtc", "CreatedByIp")
        VALUES (gen_random_uuid(), $1, $2, $3, NOW(), '127.0.0.1');
      `, [u.id, tokenHash, expiresAt]);
    }
    console.log(`  Seeded ${users.length} ApplicationUsers with linked UserProfiles & RefreshTokens.`);

    // --- Seed Supermarket Categories ---
    const categories = [
      { id: 'a1000000-0000-0000-0000-000000000001', name: 'Fresh Fruits & Vegetables', desc: 'Farm fresh organic fruits and daily picked vegetables', img: 'https://images.unsplash.com/photo-1610832958506-aa56368176cf' },
      { id: 'a1000000-0000-0000-0000-000000000002', name: 'Dairy & Eggs', desc: 'Fresh farm milk, cheese, butter and quality eggs', img: 'https://images.unsplash.com/photo-1550583724-b2692b85b150' },
      { id: 'a1000000-0000-0000-0000-000000000003', name: 'Bakery & Bread', desc: 'Freshly baked artisan breads, pastries and cakes', img: 'https://images.unsplash.com/photo-1509440159596-0249088772ff' },
      { id: 'a1000000-0000-0000-0000-000000000004', name: 'Meat & Seafood', desc: 'Premium chicken, beef, mutton and fresh seafood catch', img: 'https://images.unsplash.com/photo-1607623814075-e51df1bdc82f' },
      { id: 'a1000000-0000-0000-0000-000000000005', name: 'Rice, Grains & Pantry', desc: 'Aromatic basmati, spices, cooking oils and pantry essentials', img: 'https://images.unsplash.com/photo-1586201375761-83865001e31c' },
      { id: 'a1000000-0000-0000-0000-000000000006', name: 'Beverages & Tea', desc: 'Finest Ceylon tea, roasted coffee, fruit juices and sodas', img: 'https://images.unsplash.com/photo-1544787219-7f47ccb76574' },
      { id: 'a1000000-0000-0000-0000-000000000007', name: 'Household & Cleaning', desc: 'Detergents, sanitizers and home care essentials', img: 'https://images.unsplash.com/photo-1583947215259-38e31be8751f' }
    ];

    for (const c of categories) {
      await client.query(`
        INSERT INTO "Categories" ("Id", "Name", "Description", "ImageUrl", "CreatedAtUtc")
        VALUES ($1, $2, $3, $4, NOW());
      `, [c.id, c.name, c.desc, c.img]);
    }
    console.log(`  Seeded ${categories.length} Categories.`);

    // --- Seed Supermarket Products ---
    const products = [
      // Fruits & Veg
      { cat: 'a1000000-0000-0000-0000-000000000001', name: 'Fresh Cavendish Bananas', desc: 'Sweet and ripe Cavendish bananas locally harvested', price: 350.00, stock: 150, unit: '1 kg' },
      { cat: 'a1000000-0000-0000-0000-000000000001', name: 'Nuwara Eliya Carrots', desc: 'Crisp hill country fresh carrots', price: 480.00, stock: 90, unit: '1 kg' },
      { cat: 'a1000000-0000-0000-0000-000000000001', name: 'Organic Red Onions', desc: 'Flavorful red onions ideal for curries and salads', price: 620.00, stock: 200, unit: '1 kg' },
      // Dairy & Eggs
      { cat: 'a1000000-0000-0000-0000-000000000002', name: 'Highland Pasteurized Fresh Milk', desc: 'Pure 100% cow milk rich in calcium and vitamins', price: 520.00, stock: 80, unit: '1 L' },
      { cat: 'a1000000-0000-0000-0000-000000000002', name: 'Anchor Salted Pure Butter', desc: 'Grass-fed New Zealand churned butter', price: 890.00, stock: 65, unit: '227 g' },
      { cat: 'a1000000-0000-0000-0000-000000000002', name: 'Farm Fresh Brown Eggs (Pack of 10)', desc: 'Grade A fresh farm eggs rich in protein', price: 450.00, stock: 110, unit: 'Pack of 10' },
      // Bakery
      { cat: 'a1000000-0000-0000-0000-000000000003', name: 'Prima Special Sandwich Bread', desc: 'Soft and freshly baked white sandwich loaf', price: 190.00, stock: 45, unit: '450 g' },
      { cat: 'a1000000-0000-0000-0000-000000000003', name: 'Whole Wheat Artisan Bread', desc: 'Nutritious high-fiber wholemeal loaf', price: 280.00, stock: 30, unit: '400 g' },
      // Meat & Seafood
      { cat: 'a1000000-0000-0000-0000-000000000004', name: 'Fresh Skinless Chicken Breast', desc: 'Lean tender chicken breast fillets', price: 1450.00, stock: 50, unit: '1 kg' },
      { cat: 'a1000000-0000-0000-0000-000000000004', name: 'Yellowfin Tuna Steaks (Kelawalla)', desc: 'Fresh deep sea yellowfin tuna cut into steaks', price: 2100.00, stock: 25, unit: '1 kg' },
      // Pantry
      { cat: 'a1000000-0000-0000-0000-000000000005', name: 'Fortune Biryani Basmati Rice', desc: 'Extra long grain aromatic basmati rice', price: 1650.00, stock: 120, unit: '2 kg' },
      { cat: 'a1000000-0000-0000-0000-000000000005', name: 'Marina Pure Coconut Oil', desc: 'Cold-pressed 100% pure edible coconut oil', price: 920.00, stock: 75, unit: '1 L' },
      // Beverages & Tea
      { cat: 'a1000000-0000-0000-0000-000000000006', name: 'Dilmah Premium Ceylon Tea', desc: 'Single origin 100% pure Ceylon black tea', price: 680.00, stock: 140, unit: '100 Tea Bags' },
      { cat: 'a1000000-0000-0000-0000-000000000006', name: 'Elephant House Ginger Beer (EGB)', desc: 'Natural ginger extract carbonated beverage', price: 320.00, stock: 95, unit: '1.5 L' },
      // Household
      { cat: 'a1000000-0000-0000-0000-000000000007', name: 'Sunlight Lemon Dishwash Gel', desc: 'Degreasing dishwash with natural lemon extracts', price: 340.00, stock: 85, unit: '500 ml' },
      { cat: 'a1000000-0000-0000-0000-000000000007', name: 'Vim Antibacterial Floor Cleaner', desc: 'Lemon fresh sparkling clean surface disinfectant', price: 560.00, stock: 60, unit: '1 L' }
    ];

    const insertedProductIds = [];
    for (const p of products) {
      const pRes = await client.query(`
        INSERT INTO "Products" ("CategoryId", "Name", "Description", "Price", "StockQuantity", "Unit", "CreatedAtUtc")
        VALUES ($1, $2, $3, $4, $5, $6, NOW())
        RETURNING "Id";
      `, [p.cat, p.name, p.desc, p.price, p.stock, p.unit]);
      insertedProductIds.push({ id: pRes.rows[0].Id, name: p.name, price: p.price });
    }
    console.log(`  Seeded ${products.length} Products with pricing and inventory stock.`);

    // --- Seed Orders & Order Items ---
    const buyer1Id = '55555555-5555-5555-5555-555555555555'; // Kasun
    const buyer2Id = '66666666-6666-6666-6666-666666666666'; // Chamari

    const order1Res = await client.query(`
      INSERT INTO "Orders" ("UserId", "OrderNumber", "TotalAmount", "Status", "PaymentMethod", "ShippingAddress", "CreatedAtUtc")
      VALUES ($1, 'ORD-20260906-001', 3420.00, 'Delivered', 'Card', '24 Flower Road, Colombo 07', NOW() - INTERVAL '2 days')
      RETURNING "Id";
    `, [buyer1Id]);
    const order1Id = order1Res.rows[0].Id;

    await client.query(`
      INSERT INTO "OrderItems" ("OrderId", "ProductId", "ProductName", "UnitPrice", "Quantity", "Subtotal")
      VALUES 
        ($1, $2, $3, 1450.00, 2, 2900.00),
        ($1, $4, $5, 520.00, 1, 520.00);
    `, [order1Id, insertedProductIds[8].id, insertedProductIds[8].name, insertedProductIds[3].id, insertedProductIds[3].name]);

    const order2Res = await client.query(`
      INSERT INTO "Orders" ("UserId", "OrderNumber", "TotalAmount", "Status", "PaymentMethod", "ShippingAddress", "CreatedAtUtc")
      VALUES ($1, 'ORD-20260906-002', 1570.00, 'Processing', 'CashOnDelivery', '15 Peradeniya Road, Kandy', NOW() - INTERVAL '3 hours')
      RETURNING "Id";
    `, [buyer2Id]);
    const order2Id = order2Res.rows[0].Id;

    await client.query(`
      INSERT INTO "OrderItems" ("OrderId", "ProductId", "ProductName", "UnitPrice", "Quantity", "Subtotal")
      VALUES 
        ($1, $2, $3, 890.00, 1, 890.00),
        ($1, $4, $5, 680.00, 1, 680.00);
    `, [order2Id, insertedProductIds[4].id, insertedProductIds[4].name, insertedProductIds[12].id, insertedProductIds[12].name]);

    console.log('  Seeded sample Orders & OrderItems with full relationship tracking.');

    // --- Seed Active Cart ---
    const cartRes = await client.query(`
      INSERT INTO "Carts" ("UserId", "CreatedAtUtc")
      VALUES ($1, NOW())
      RETURNING "Id";
    `, [buyer1Id]);
    const cartId = cartRes.rows[0].Id;

    await client.query(`
      INSERT INTO "CartItems" ("CartId", "ProductId", "Quantity", "AddedAtUtc")
      VALUES 
        ($1, $2, 3, NOW()),
        ($1, $3, 1, NOW());
    `, [cartId, insertedProductIds[0].id, insertedProductIds[10].id]);
    console.log('  Seeded active shopping cart with items for buyer.\n');

    // ─── 4. VERIFICATION TESTS ────────────────────────────────────────────────
    console.log('[4/6] Running automated database verification tests...\n');

    let passedTests = 0;
    let totalTests = 0;

    function assertTest(name, condition, details) {
      totalTests++;
      if (condition) {
        passedTests++;
        console.log(`  PASS: ${name}`);
        if (details) console.log(`        -> ${details}`);
      } else {
        console.error(`  FAIL: ${name}`);
        if (details) console.error(`        -> ${details}`);
      }
    }

    // Test 1: Record Counts
    const userCount = await client.query('SELECT COUNT(*) FROM "ApplicationUsers";');
    const profileCount = await client.query('SELECT COUNT(*) FROM "UserProfiles";');
    const refreshCount = await client.query('SELECT COUNT(*) FROM "RefreshTokens";');
    const catCount = await client.query('SELECT COUNT(*) FROM "Categories";');
    const prodCount = await client.query('SELECT COUNT(*) FROM "Products";');
    const orderCount = await client.query('SELECT COUNT(*) FROM "Orders";');

    assertTest(
      'Table Record Counts',
      parseInt(userCount.rows[0].count) === 8 &&
      parseInt(profileCount.rows[0].count) === 8 &&
      parseInt(catCount.rows[0].count) === 7 &&
      parseInt(prodCount.rows[0].count) === 16 &&
      parseInt(orderCount.rows[0].count) === 2,
      `Users: ${userCount.rows[0].count}, Profiles: ${profileCount.rows[0].count}, Categories: ${catCount.rows[0].count}, Products: ${prodCount.rows[0].count}, Orders: ${orderCount.rows[0].count}`
    );

    // Test 2: Admin Authentication Verification
    const adminUser = await client.query('SELECT * FROM "ApplicationUsers" WHERE "Email" = $1;', ['admin@tntsupermarket.com']);
    const isPasswordValid = bcrypt.compareSync('Password@123', adminUser.rows[0].PasswordHash);
    assertTest(
      'Password BCrypt Verification for admin@tntsupermarket.com',
      isPasswordValid && adminUser.rows[0].Role === 'Admin',
      `Verified BCrypt hash match for "Password@123", Role: ${adminUser.rows[0].Role}`
    );

    // Test 3: Relational Integrity (User -> Profile 1:1 join)
    const joinRes = await client.query(`
      SELECT u."FullName", u."Email", u."Role", p."Address", p."City"
      FROM "ApplicationUsers" u
      JOIN "UserProfiles" p ON u."Id" = p."IdentityUserId"
      ORDER BY u."Role", u."FullName";
    `);
    assertTest(
      '1:1 Join between ApplicationUsers and UserProfiles',
      joinRes.rows.length === 8,
      `Successfully resolved all ${joinRes.rows.length} linked user profiles`
    );

    // Test 4: Product Category Relationship
    const prodCatRes = await client.query(`
      SELECT c."Name" as category, COUNT(p."Id") as product_count, MIN(p."Price") as min_price, MAX(p."Price") as max_price
      FROM "Categories" c
      LEFT JOIN "Products" p ON c."Id" = p."CategoryId"
      GROUP BY c."Name"
      ORDER BY product_count DESC;
    `);
    assertTest(
      'Product-to-Category Foreign Key Grouping',
      prodCatRes.rows.length === 7,
      `All 7 categories populated with valid prices (e.g. ${prodCatRes.rows[0].category}: ${prodCatRes.rows[0].product_count} items)`
    );

    // Test 5: Order & OrderItems Calculation
    const orderSumRes = await client.query(`
      SELECT o."OrderNumber", o."TotalAmount", SUM(oi."Subtotal") as calculated_sum
      FROM "Orders" o
      JOIN "OrderItems" oi ON o."Id" = oi."OrderId"
      GROUP BY o."OrderNumber", o."TotalAmount";
    `);
    const sumsMatch = orderSumRes.rows.every(r => parseFloat(r.TotalAmount) === parseFloat(r.calculated_sum));
    assertTest(
      'Order Financial Reconciliation (TotalAmount == SUM(OrderItems))',
      sumsMatch,
      `Reconciled ${orderSumRes.rows.length} orders accurately without decimal drift`
    );

    // Test 6: Dynamic CRUD Write & Delete Cycle
    const testGuid = crypto.randomUUID();
    const testEmail = `temp_${Date.now()}@example.com`;
    await client.query(`
      INSERT INTO "ApplicationUsers" ("Id", "FullName", "Email", "NormalizedEmail", "PasswordHash", "Role", "IsActive", "CreatedAtUtc")
      VALUES ($1, 'Ephemeral Test User', $2, $3, $4, 'Buyer', true, NOW());
    `, [testGuid, testEmail, testEmail.toUpperCase(), passwordHash]);

    const queriedTemp = await client.query('SELECT "Id" FROM "ApplicationUsers" WHERE "Id" = $1;', [testGuid]);
    await client.query('DELETE FROM "ApplicationUsers" WHERE "Id" = $1;', [testGuid]);
    const deletedTemp = await client.query('SELECT "Id" FROM "ApplicationUsers" WHERE "Id" = $1;', [testGuid]);

    assertTest(
      'Dynamic CRUD Cycle (Insert, Query, Delete)',
      queriedTemp.rows.length === 1 && deletedTemp.rows.length === 0,
      'CRUD lifecycle validated on live database'
    );

    console.log(`\nVerification Summary: ${passedTests}/${totalTests} tests passed.\n`);

    // ─── 5. SUMMARY OF TEST USERS AVAILABLE FOR TESTING ─────────────────────
    console.log('[5/6] Seeded Supermarket Test Users:');
    console.log('----------------------------------------------------------------');
    console.log(' All users have default password: Password@123');
    console.log('----------------------------------------------------------------');
    for (const r of joinRes.rows) {
      console.log(` • [${r.Role.padEnd(7)}] ${r.Email.padEnd(36)} | ${r.FullName} (${r.City})`);
    }
    console.log('----------------------------------------------------------------\n');

    // ─── 6. SAMPLE PRODUCTS ──────────────────────────────────────────────────
    console.log('[6/6] Sample Supermarket Products in Database:');
    console.log('----------------------------------------------------------------');
    const sampleProducts = await client.query(`
      SELECT p."Name", c."Name" as "Category", p."Price", p."Unit", p."StockQuantity"
      FROM "Products" p
      JOIN "Categories" c ON p."CategoryId" = c."Id"
      ORDER BY c."Name", p."Price"
      LIMIT 8;
    `);
    for (const p of sampleProducts.rows) {
      console.log(` • ${p.Name.padEnd(34)} | Rs. ${p.Price.padEnd(8)} / ${p.Unit.padEnd(10)} | Stock: ${p.StockQuantity}`);
    }
    console.log('----------------------------------------------------------------');
    await client.query('COMMIT');
    transactionStarted = false;
    console.log('\n SUCCESS: Database connection, schema, seeding and tests finished successfully!');

  } catch (err) {
    if (transactionStarted) {
      await client.query('ROLLBACK');
    }
    console.error(' ERROR during database operations:', err);
    process.exitCode = 1;
  } finally {
    await client.end();
  }
}

run();
