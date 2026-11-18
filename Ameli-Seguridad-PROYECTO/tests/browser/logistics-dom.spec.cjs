const { test } = require("node:test");
const assert = require("node:assert/strict");
const { randomUUID } = require("node:crypto");
const { readFile } = require("node:fs/promises");
const { JSDOM, VirtualConsole } = require("jsdom");

test("GOL: formularios DOM con Web, API y SQL reales; categorías, disponibilidad, preparación, despacho y entrega", { skip: !process.env.AMELI_API_URL || !process.env.AMELI_E2E_ADMIN_PASSWORD }, async () => {
  const api = process.env.AMELI_API_URL, web = process.env.AMELI_WEB_URL, password = process.env.AMELI_E2E_ADMIN_PASSWORD;
  const adminEmail = process.env.AMELI_E2E_ADMIN_EMAIL || "admin@ameli-e2e.test", suffix = randomUUID().slice(0, 8), doms = [];
  async function call(path, body, token, method = body === undefined ? "GET" : "POST") {
    const response = await fetch(api + path, { method, headers: { "Content-Type": "application/json", ...(token ? { Authorization: "Bearer " + token } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
    assert.ok(response.ok, `${method} ${path}: ${response.status} ${response.ok ? "" : await response.text()}`); return response.json();
  }
  async function wait(check, label) {
    for (let i = 0; i < 180; i++) { if (await check()) return; await new Promise(resolve => setTimeout(resolve, 50)); }
    const errors = doms.flatMap(dom => dom.errors); throw new Error("Timed out: " + label + " " + errors.join("\n"));
  }
  class WebClient {
    cookies = new Map();
    async fetch(path, options = {}) {
      const headers = new Headers(options.headers); headers.set("Cookie", [...this.cookies].map(([k, v]) => `${k}=${v}`).join("; "));
      const response = await fetch(new URL(path, web), { ...options, headers, redirect: "manual" });
      for (const cookie of response.headers.getSetCookie()) { const pair = cookie.split(";")[0], at = pair.indexOf("="); this.cookies.set(pair.slice(0, at), pair.slice(at + 1)); }
      return response;
    }
    async login(email) {
      const html = await (await this.fetch("/ingresar")).text();
      const token = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)[1];
      const response = await this.fetch("/account/login", { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: new URLSearchParams({ email, password, __RequestVerificationToken: token }) });
      assert.equal(response.status, 302); assert.ok(!response.headers.get("location").includes("error="));
    }
    async dom(path) {
      const response = await this.fetch(path); assert.equal(response.status, 200); const html = await response.text();
      const sources = [];
      for (const name of ["assistant.js", "identity.js", "shop.js", "products.js", "catalog-lifecycle.js", "fulfillment.js"]) {
        let text = await (await this.fetch("/js/" + name)).text();
        if (name === "assistant.js") text = text.slice(0, text.indexOf('document.addEventListener("DOMContentLoaded"'));
        sources.push(text);
      }
      const dom = new JSDOM(html, { url: new URL(path, web).href, runScripts: "outside-only", virtualConsole: new VirtualConsole() });
      dom.errors = []; dom.window.addEventListener("error", event => dom.errors.push(event.error?.stack || event.message));
      dom.window.fetch = (url, options) => this.fetch(url, options); dom.window.confirm = () => true;
      dom.window.eval(sources.join("\n;\n")); doms.push(dom); return dom;
    }
  }
  const submit = (dom, form) => form.dispatchEvent(new dom.window.Event("submit", { bubbles: true, cancelable: true }));
  try {
    const token = (await call("auth/login", { email: adminEmail, password })).accessToken;
    const buyerToken = (await call("auth/login", { email: "cliente@ameli.test", password })).accessToken;
    if (process.env.AMELI_E2E_LEGACY_STATE) {
      const legacy = JSON.parse(await readFile(process.env.AMELI_E2E_LEGACY_STATE, "utf8"));
      const order = await call(`assistant/orders/${legacy.order.number}`, undefined, buyerToken);
      assert.equal(order.total, legacy.order.total); assert.equal(order.deliverySnapshot, legacy.order.deliverySnapshot);
      assert.equal(order.fulfillment.paymentStatus, "Pagado"); assert.equal(order.items[0].reservedQuantity, order.items[0].quantity);
      console.log("Actualización desde las siete migraciones previas: pedido, pago y reserva conservados.");
    }
    const category = await call("admin/catalog/categories", { name: "Ropa " + suffix, isActive: true }, token);
    const duplicate = await call("admin/catalog/categories", { name: "Otra " + suffix, isActive: true }, token);
    const supplier = await call("admin/catalog/suppliers", { name: "Taller " + suffix, email: suffix + "@example.test", phone: "88887777", isActive: true }, token);
    const product = await call("admin/catalog/products", { name: "Producto " + suffix, categoryId: category.id, supplierId: supplier.id, species: "Perro", material: "Algodón", unitPrice: 1000, stockQuantity: 8, isActive: true }, token);
    const address = await call("customer/addresses", { province: "San José", canton: "San José", district: "Carmen", exactAddress: "Casa azul", reference: "Frente al parque", isDefault: true }, buyerToken);
    await call("assistant/cart", { requestId: randomUUID(), productId: product.id, quantity: 2, acknowledgesUnspecifiedSize: true }, buyerToken);
    const order = await call("assistant/checkout", { requestId: randomUUID(), addressId: address.id, cardNumber: "4242424242424242", expirationDate: "12/39", cardholderName: "Cliente Prueba", cvc: "123" }, buyerToken);
    const administrator = new WebClient(); await administrator.login(adminEmail);
    const categoryDom = await administrator.dom(`/administracion/catalogos/categorias/${category.id}/editar`), cd = categoryDom.window.document;
    await wait(() => cd.querySelector('[name="Name"]').value === category.name, "category editor");
    cd.querySelector('[name="Name"]').value = duplicate.name; submit(categoryDom, cd.getElementById("category-edit-form"));
    await wait(() => cd.querySelector('[data-error="Name"]').textContent.includes("duplicado"), "duplicate retained");
    assert.equal(cd.querySelector('[name="Name"]').value, duplicate.name);
    cd.querySelector('[name="Name"]').value = "Ropa editada " + suffix; submit(categoryDom, cd.getElementById("category-edit-form"));
    await wait(() => cd.getElementById("category-edit-result").textContent.includes("actualizada correctamente"), "category saved");
    const ca = cd.querySelector("[data-availability-kind]");
    await wait(() => !ca.querySelector('[type="submit"]').disabled, "category availability loaded");
    ca.querySelector('[name="Reason"]').value = "Reorganizar catálogo"; submit(categoryDom, ca.querySelector("form"));
    await wait(() => ca.querySelector('[data-error="IsActive"]').textContent.includes("1"), "active category blocked");
    const productDom = await administrator.dom(`/administracion/catalogos/productos/${product.id}/editar`), pd = productDom.window.document;
    const pa = pd.querySelector("[data-availability-kind]");
    await wait(() => pa.querySelector('[data-availability-state]').textContent.includes("Activo") && !pa.querySelector('[type="submit"]').disabled, "product availability");
    pa.querySelector('[name="Reason"]').value = "Retiro de prueba"; submit(productDom, pa.querySelector("form"));
    await wait(() => pa.querySelector('[data-availability-state]').textContent.includes("Inactivo"), "product inactive");
    const saved = await call(`assistant/orders/${order.number}`, undefined, buyerToken); assert.equal(saved.items[0].productName, product.name);
    submit(categoryDom, ca.querySelector("form"));
    await wait(() => ca.querySelector('[data-availability-state]').textContent.includes("Inactivo"), "category inactive");
    submit(productDom, pa.querySelector("form"));
    await wait(() => pa.querySelector('[data-error="CategoryId"]').textContent.includes("activa"), "product reactivation blocked");
    submit(categoryDom, ca.querySelector("form"));
    await wait(() => ca.querySelector('[data-availability-state]').textContent.includes("Activo"), "category reactivated");
    submit(productDom, pa.querySelector("form"));
    await wait(() => pa.querySelector('[data-availability-state]').textContent.includes("Activo"), "product reactivated");
    console.log("Formularios de categoría y producto: edición, duplicado retenido, motivos, bloqueos y reactivación aprobados.");

    const logisticsName = (await call("auth/login", { email: "logistica@ameli.test", password })).status.user.name;
    const logistics = new WebClient(); await logistics.login("logistica@ameli.test");
    const operation = await logistics.dom("/gestion/pedidos"), document = operation.window.document;
    const card = () => document.querySelector(`[data-order-id="${order.id}"]`);
    const action = name => card()?.querySelector(`[data-action="${name}"]`);
    await wait(() => action("prepare"), "preparation available"); submit(operation, action("prepare"));
    await wait(() => action("packing"), "preparation taken");
    assert.ok(card().textContent.includes("Responsable: " + logisticsName));
    action("packing").elements.namedItem(order.items[0].id).value = "2"; submit(operation, action("packing"));
    await wait(() => action("packing")?.elements.namedItem(order.items[0].id).value === "2" && !action("packing").querySelector('[type="submit"]').disabled, "packing saved");
    action("dispatch").elements.namedItem("TrackingNumber").value = "GOL-" + suffix;
    assert.equal(action("dispatch").elements.namedItem("Carrier").checkValidity(), false);
    action("dispatch").elements.namedItem("Carrier").value = "Correos"; submit(operation, action("dispatch"));
    await wait(() => action("deliver"), "dispatched");
    function fillDelivery(quantity) {
      const form = action("deliver"); form.elements.namedItem(order.items[0].id).value = String(quantity);
      const time = new Date(Date.now() + 60000);
      form.elements.namedItem("DeliveredAtUtc").value = new Date(time.getTime() - time.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
      form.elements.namedItem("CarrierConfirmation").value = "POD-" + suffix; form.elements.namedItem("ReceiptReference").value = "Recibido por cliente";
    }
    fillDelivery(1); submit(operation, action("deliver"));
    await wait(() => action("deliver")?.textContent.includes("No se permiten entregas parciales"), "partial delivery blocked");
    assert.equal(action("deliver").elements.namedItem(order.items[0].id).value, "1");
    action("incidents").elements.namedItem("Detail").value = "Falta una unidad en el paquete"; submit(operation, action("incidents"));
    await wait(() => card()?.textContent.includes("Incidencia: Falta una unidad"), "incident recorded");
    fillDelivery(2); submit(operation, action("deliver"));
    await wait(() => card()?.textContent.includes("Seguimiento cerrado"), "delivery recorded");
    assert.ok(card().textContent.includes("Registrada por " + logisticsName));
    const customer = new WebClient(); await customer.login("cliente@ameli.test"); const own = await customer.dom("/mis-pedidos");
    const ownCard = () => own.window.document.querySelector(`[data-order-id="${order.id}"]`);
    await wait(() => ownCard()?.querySelector('[data-action="rating"]'), "rating enabled after delivery");
    assert.match(ownCard().textContent, /En preparación.*En camino.*Entregado/s);
    assert.match(ownCard().textContent, /programa de puntos/);
    const rating = ownCard().querySelector('[data-action="rating"]'); rating.elements.namedItem("Stars").value = "4";
    rating.elements.namedItem("Comment").value = "Entrega completa"; submit(own, rating);
    await wait(() => ownCard()?.textContent.includes("Tu calificación: 4/5"), "rating persisted");
    const denied = await logistics.fetch(`/administracion/catalogos/categorias/${category.id}/editar`);
    assert.equal(denied.status, 302); assert.equal(denied.headers.get("location"), "/sin-acceso");
    for (const dom of doms) assert.deepEqual(dom.errors, []);
    console.log("Formularios logísticos y cliente: preparación, empaque, despacho, entrega parcial bloqueada, incidencia, entrega y calificación aprobados.");
  } finally { for (const dom of doms) dom.window.close(); }
});
