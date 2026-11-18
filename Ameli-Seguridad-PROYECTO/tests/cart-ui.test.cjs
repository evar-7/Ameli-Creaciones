const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { webcrypto } = require("node:crypto");

// Test the shipped JavaScript with form/transport adapters; these are not browser or SQL tests.
function setup(respond, { size = "S", petId = "", consent = false, valid = true, stock = 30 } = {}) {
  const button = { disabled: false, textContent: "Agregar al carrito" };
  const feedback = { textContent: "", links: [], append(node) { this.links.push(node); } };
  const controls = {
    Size: { value: size, disabled: Boolean(petId) },
    Quantity: { value: "1", max: String(stock), disabled: false },
    ...(consent ? { AcknowledgesUnspecifiedSize: { checked: true, disabled: false } } : {})
  };
  let submit;
  const form = {
    dataset: { productId: "product-1", petId },
    elements: { namedItem: name => controls[name] || null },
    querySelector: selector => selector.startsWith("button") ? button : feedback,
    reportValidity: () => valid,
    addEventListener(type, handler) { assert.equal(type, "submit"); submit = handler; }
  };
  const calls = [], window = {};
  const document = {
    addEventListener() {},
    querySelector: () => ({ value: "csrf-token" }),
    createElement: tag => ({ tag, textContent: "" })
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../src/Ameli.Web/wwwroot/js/assistant.js"), "utf8"), {
    window, document, crypto: webcrypto, Intl, Date,
    fetch: async (url, init) => {
      const call = { url, ...init, payload: JSON.parse(init.body) }; calls.push(call);
      return respond(call, calls.length);
    }
  });
  window.ameliShop.bindCartForm(form);
  return { controls, calls, button, feedback, submit: () => submit({ preventDefault() {} }) };
}
const ok = () => ({ ok: true, status: 200, json: async () => ({ message: "Producto agregado al carrito." }) });
const failure = status => ({ ok: false, status, json: async () => ({ code: "invalid_size", message: "Revisa tu selección." }) });

test("Manual purchase sends consent, an empty size, quantity and the antiforgery token", async () => {
  const ui = setup(ok, { size: "", consent: true });
  ui.controls.Quantity.value = "2";
  await ui.submit();
  assert.equal(ui.calls.length, 1);
  assert.equal(ui.calls[0].url, "/account/shop/cart");
  assert.equal(ui.calls[0].headers["X-CSRF-TOKEN"], "csrf-token");
  assert.equal(ui.calls[0].payload.size, "");
  assert.equal(ui.calls[0].payload.quantity, 2);
  assert.equal(ui.calls[0].payload.acknowledgesUnspecifiedSize, true);
  assert.equal(ui.calls[0].payload.petId, null);
  assert.equal(ui.feedback.links[0].href, "/carrito");
});

test("An invalid form makes no request", async () => {
  const ui = setup(ok, { valid: false });
  await ui.submit();
  assert.equal(ui.calls.length, 0);
});

test("Excess quantity shows the required stock message before submitting", async () => {
  const ui = setup(ok, { stock: 3 });
  ui.controls.Quantity.value = "4";
  await ui.submit();
  assert.equal(ui.calls.length, 0);
  assert.equal(ui.feedback.textContent, "Existencias de artículo superadas");
});

test("A successful addition displays the subtotal and total returned by the API", async () => {
  const ui = setup(() => ({ ok: true, status: 200, json: async () => ({ message: "Producto agregado al carrito.", cart: { subtotal: 200, total: 226 } }) }));
  await ui.submit();
  assert.match(ui.feedback.textContent, /Subtotal:.*200.*Total:.*226/);
});

test("Double submission sends once; an intentional second purchase has a fresh request ID", async () => {
  let complete;
  const ui = setup(() => new Promise(resolve => complete = resolve));
  const first = ui.submit();
  await ui.submit();
  assert.equal(ui.calls.length, 1);
  assert.equal(ui.button.disabled, true);
  complete(ok()); await first;
  assert.equal(ui.button.disabled, false);
  const second = ui.submit(); complete(ok()); await second;
  assert.equal(ui.calls.length, 2);
  assert.notEqual(ui.calls[0].payload.requestId, ui.calls[1].payload.requestId);
});

test("A lost response retries the exact same request and locks selection until it resolves", async () => {
  const ui = setup((_, count) => { if (count === 1) throw new TypeError("Conexión interrumpida"); return ok(); });
  await ui.submit();
  assert.equal(ui.controls.Size.disabled, true);
  assert.equal(ui.controls.Quantity.disabled, true);
  assert.equal(ui.button.disabled, false);
  await ui.submit();
  assert.deepEqual(ui.calls[1].payload, ui.calls[0].payload);
  assert.equal(ui.controls.Size.disabled, false);
  assert.equal(ui.controls.Quantity.disabled, false);
});

test("HTTP 5xx keeps the request ID because the server may have already saved the cart", async () => {
  const ui = setup((_, count) => count === 1 ? failure(500) : ok());
  await ui.submit(); await ui.submit();
  assert.deepEqual(ui.calls[1].payload, ui.calls[0].payload);
});

test("A business rejection unlocks fields and allows a corrected size with a fresh ID", async () => {
  const ui = setup((_, count) => count === 1 ? failure(400) : ok());
  await ui.submit();
  assert.equal(ui.controls.Size.disabled, false);
  ui.controls.Size.value = "M";
  await ui.submit();
  assert.equal(ui.calls[1].payload.size, "M");
  assert.notEqual(ui.calls[0].payload.requestId, ui.calls[1].payload.requestId);
});

test("Recommended purchases preserve their pet and fixed size after success", async () => {
  const ui = setup(ok, { petId: "pet-1" });
  await ui.submit();
  assert.equal(ui.calls[0].payload.petId, "pet-1");
  assert.equal(ui.calls[0].payload.size, "S");
  assert.equal(ui.calls[0].payload.acknowledgesUnspecifiedSize, false);
  assert.equal(ui.controls.Size.disabled, true);
  assert.equal(ui.controls.Quantity.disabled, false);
});

function renderShop() {
  const descendants = node => node.children.flatMap(child => [child, ...descendants(child)]);
  const node = tag => ({
    tag, children: [], dataset: {}, textContent: "", value: "", disabled: false,
    append(...children) { this.children.push(...children); },
    setAttribute(name, value) { this[name] = value; },
    addEventListener() {},
    get elements() { return { namedItem: name => descendants(this).find(child => child.name === name) }; },
    querySelector(selector) {
      return descendants(this).find(child => selector.startsWith("button")
        ? child.tag === "button" && child.type === "submit" : child.className === selector.slice(1));
    }
  });
  const window = {}, document = { createElement: node, createTextNode: text => ({ children: [], textContent: text }), addEventListener() {} };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../src/Ameli.Web/wwwroot/js/assistant.js"), "utf8"), { window, document, Intl, Date });
  return { shop: window.ameliShop, descendants };
}
const sample = sizes => ({ id: "product-1", name: "Accesorio", sku: "AML-CART", unitPrice: 1250,
  stockQuantity: 3, sizes, colors: [], updatedAtUtc: "2026-10-20T00:00:00Z", productUrl: "/productos/product-1", guideUrl: "/productos/product-1/guia" });

test("A chat catalog card without a size table exposes a purchase form and requires explicit consent", () => {
  const { shop, descendants } = renderShop();
  const card = shop.productCard({ product: sample([]) }, true);
  const form = descendants(card).find(node => node.tag === "form");
  assert.ok(form, "The purchase button must not disappear when sizes are absent");
  assert.equal(form.elements.namedItem("AcknowledgesUnspecifiedSize").required, true);
  assert.equal(form.elements.namedItem("Quantity").max, "3");
  assert.equal(form.elements.namedItem("Size"), undefined);
});

test("A recommendation form retains its originating pet and its suggested size", () => {
  const { shop } = renderShop();
  const form = shop.createCartForm(sample([{ name: "S" }]), { petId: "pet-1", size: "S" });
  assert.equal(form.dataset.petId, "pet-1");
  assert.equal(form.elements.namedItem("Size").value, "S");
  assert.equal(form.elements.namedItem("Size").disabled, true);
  assert.equal(form.elements.namedItem("AcknowledgesUnspecifiedSize"), undefined);
});

test("An anonymous catalog card links to login and never creates a purchase form", () => {
  const { shop, descendants } = renderShop();
  const card = shop.productCard({ product: sample([]) }, false);
  assert.equal(descendants(card).some(node => node.tag === "form"), false);
  assert.ok(descendants(card).some(node => node.tag === "a" && node.href === "/ingresar"));
});
