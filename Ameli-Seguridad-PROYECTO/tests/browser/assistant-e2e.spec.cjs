const { test } = require("node:test");
const assert = require("node:assert/strict");
const { randomUUID } = require("node:crypto");
const { chromium } = require("playwright");

test("AAC: real web/API flow from registration to recommendation, cart and order tracking", { skip: !process.env.AMELI_API_URL || !process.env.AMELI_E2E_ADMIN_PASSWORD }, async () => {
  const api = process.env.AMELI_API_URL, web = process.env.AMELI_WEB_URL;
  const password = process.env.AMELI_E2E_ADMIN_PASSWORD, suffix = randomUUID().slice(0, 8);
  const call = async (path, body, token, method = "POST") => {
    const response = await fetch(api + path, { method, headers: { "Content-Type": "application/json", ...(token ? { Authorization: "Bearer " + token } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
    assert.ok(response.ok, `${method} ${path}: HTTP ${response.status}`); return response.json();
  };
  const admin = await call("auth/login", { email: process.env.AMELI_E2E_ADMIN_EMAIL || "admin@ameli-e2e.test", password });
  const token = admin.accessToken;
  const category = await call("admin/catalog/categories", { name: "Ropa " + suffix, isActive: true }, token);
  const supplier = await call("admin/catalog/suppliers", { name: "Taller " + suffix, email: `taller-${suffix}@example.test`, phone: "88112233", isActive: true }, token);
  const product = await call("admin/catalog/products", { name: "Suéter " + suffix, categoryId: category.id, supplierId: supplier.id, species: "Perro", material: "Algodón", unitPrice: 10000, stockQuantity: 10, isActive: true }, token);
  await call(`assistant/admin/products/${product.id}`, { sku: "AML-" + suffix.toUpperCase(), material: "Algodón", careInstructions: "Lavado manual", fitNotes: "Ajuste holgado", colors: ["Azul"], garmentType: "Suéter", style: "Clásico", unitPrice: 10000, stockQuantity: 10, isActive: true,
    sizes: [{ name: "S", minHeightCm: 20, maxHeightCm: 40, minWeightKg: 2, maxWeightKg: 10, equivalent: "Pequeña" }] }, token, "PUT");
  const browser = await chromium.launch({ executablePath: process.env.AMELI_BROWSER_EXECUTABLE || undefined, headless: true, args: ["--no-sandbox"] });
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } }); const page = await context.newPage();
  const errors = []; page.on("pageerror", error => errors.push(error.message));
  try {
    await page.goto(web + "/registro");
    for (const [name, value] of Object.entries({ FirstName: "Ana", LastNames: "Prueba", Email: `cliente-${suffix}@example.test`, Phone: "88112233", Password: password, ConfirmPassword: password })) await page.locator(`#customer-registration [name="${name}"]`).fill(value);
    await page.locator('#customer-registration button[type="submit"]').click(); await page.waitForURL("**/ingresar?estado=registrada");
    await page.locator("#login-email").fill(`cliente-${suffix}@example.test`); await page.locator("#login-password").fill(password);
    await page.locator('form[action="/account/login"] button[type="submit"]').click(); await page.waitForURL("**/cliente");
    assert.equal(await page.locator("#ameli-chat").getAttribute("data-client"), "true");
    await page.goto(web + "/perfil/mascotas/nueva");
    for (const [name, value] of Object.entries({ Name: "Luna " + suffix, Species: "Perro", Breed: "Mestiza", AgeYears: "2", WeightKg: "8", HeightCm: "30" })) await page.locator(`#pet-form [name="${name}"]`).fill(value);
    await page.locator('#pet-form [name="AllergiesDeclared"]').check(); await page.locator('#pet-form button[type="submit"]').click(); await page.waitForURL(/\/perfil\/mascotas\/[0-9a-f-]+$/);
    await page.goto(web + "/perfil/direcciones");
    for (const [name, value] of Object.entries({ Province: "San José", Canton: "San José", District: "Carmen", ExactAddress: "Casa azul", Reference: "Frente al parque" })) await page.locator(`#address-form [name="${name}"]`).fill(value);
    await page.locator('#address-form button[type="submit"]').click(); await page.waitForFunction(() => document.getElementById("address-list").textContent.includes("Casa azul"));
    await page.locator("#chat-launch").click(); await page.waitForFunction(() => document.getElementById("chat-pet").options.length > 1);
    await page.locator("#chat-intent").selectOption("recommendations"); await page.locator("#chat-pet").selectOption({ label: "Luna " + suffix }); await page.locator("#chat-send-button").click();
    await page.waitForFunction(() => document.getElementById("chat-status").textContent === "Consulta respondida.");
    const card = page.locator(".chat-product").filter({ hasText: product.name }); assert.equal(await card.count(), 1);
    assert.match(await card.textContent(), /Talla sugerida: S/); await card.getByRole("button", { name: "Agregar al carrito" }).click(); await card.getByRole("button", { name: "Agregado", exact: true }).waitFor();
    await page.goto(web + "/carrito"); await page.waitForFunction(() => document.getElementById("shop-cart").textContent.includes("Suéter"));
    page.once("dialog", dialog => dialog.accept()); await page.locator("#checkout-button").click(); await page.waitForURL("**/mis-pedidos?numero=*");
    await page.locator("#shop-orders h2").waitFor(); const number = await page.locator("#shop-orders h2").first().textContent();
    assert.match(number, /^AML-[A-F0-9]{32}$/);
    const all = await call("assistant/staff/orders", undefined, token, "GET"); const order = all.find(o => o.number === number); assert.ok(order);
    await call(`assistant/staff/orders/${order.id}`, { status: "Enviado", carrier: "Correos", trackingNumber: "CR-123" }, token, "PUT");
    await page.locator("#chat-launch").click(); await page.locator("#chat-intent").selectOption("order"); await page.locator('#chat-tools [name="OrderNumber"]').fill(number); await page.locator("#chat-send-button").click();
    await page.waitForFunction(() => document.getElementById("chat-status").textContent === "Consulta respondida."); assert.match(await page.locator("#chat-log").textContent(), /CR-123/);
    assert.deepEqual(errors, []);
  } finally { await context.close(); await browser.close(); }
});
