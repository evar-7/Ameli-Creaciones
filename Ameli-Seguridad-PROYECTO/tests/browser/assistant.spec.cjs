const { test } = require("node:test");
const assert = require("node:assert/strict");
const { chromium } = require("playwright");

test("AAC005: responsive chat, 4 second status, cancellation, deduplication and offline retry", async () => {
  const browser = await chromium.launch({ executablePath: process.env.AMELI_BROWSER_EXECUTABLE || undefined, headless: true, args: ["--no-sandbox"] });
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 800 } });
  const page = await context.newPage(); let calls = 0, slow = false;
  const errors = []; page.on("pageerror", e => errors.push(e.message));
  await page.route("**/account/assistant/products?*", route => route.fulfill({ json: [] }));
  await page.route("**/account/assistant/pets", route => route.fulfill({ json: [] }));
  await page.route("**/account/assistant/messages", async route => {
    calls++; const payload = route.request().postDataJSON();
    if (slow) await new Promise(resolve => setTimeout(resolve, 5200));
    try { await route.fulfill({ json: { requestId: payload.requestId, kind: "catalog", message: "Respuesta única", products: [], missingFields: [] } }); } catch { /* A canceled request must not add a message. */ }
  });
  try {
    await page.goto((process.env.AMELI_WEB_URL || "https://localhost:7340") + "/ingresar");
    await page.locator("#chat-launch").click();
    for (const viewport of [{ width: 390, height: 844 }, { width: 768, height: 1024 }, { width: 1280, height: 800 }, { width: 812, height: 375 }]) {
      await page.setViewportSize(viewport);
      for (const selector of ["#chat-message", "#chat-send-button", "#chat-close", "#chat-font"]) {
        const box = await page.locator(selector).boundingBox(); assert.ok(box && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, `${selector} visible at ${viewport.width}×${viewport.height}`);
      }
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator("#chat-message").fill("Consulta rápida");
    await page.evaluate(() => { document.getElementById("chat-send").requestSubmit(); document.getElementById("chat-send").requestSubmit(); });
    await page.waitForFunction(() => document.getElementById("chat-status").textContent === "Consulta respondida."); assert.equal(calls, 1);
    slow = true; await page.locator("#chat-message").fill("Consulta lenta"); await page.locator("#chat-send-button").click();
    await page.waitForFunction(() => document.getElementById("chat-status").textContent.includes("Sigo procesando"), null, { timeout: 6000 });
    await page.locator("#chat-cancel").click(); assert.match(await page.locator("#chat-status").textContent(), /cancelada/);
    const beforeRetry = await page.locator(".chat-bubble:not(.user)").count();
    slow = false; await page.locator("#chat-retry").click(); await page.waitForFunction(() => document.getElementById("chat-status").textContent === "Consulta respondida.");
    await page.waitForTimeout(1600); assert.equal(await page.locator(".chat-bubble:not(.user)").count(), beforeRetry + 1);
    assert.equal(await page.locator(".chat-bubble.user").count(), 2, "Retry must reuse the user message");
    const beforeOffline = calls; await context.setOffline(true); await page.locator("#chat-message").fill("Texto guardado sin Internet"); await page.locator("#chat-send-button").click();
    assert.match(await page.locator("#chat-status").textContent(), /no fue enviada/); assert.equal(calls, beforeOffline);
    assert.ok(await page.evaluate(() => sessionStorage.getItem("ameli-chat-draft:visitante")?.includes("Texto guardado sin Internet")));
    await context.setOffline(false); await page.locator("#chat-retry").click(); await page.waitForFunction(() => document.getElementById("chat-status").textContent === "Consulta respondida.");
    assert.equal(calls, beforeOffline + 1); assert.equal(await page.evaluate(() => sessionStorage.getItem("ameli-chat-draft:visitante")), null);
    assert.deepEqual(errors, []);
    if (process.env.AMELI_UI_SCREENSHOT) await page.screenshot({ path: process.env.AMELI_UI_SCREENSHOT, fullPage: true });
  } finally { await context.close(); await browser.close(); }
});
