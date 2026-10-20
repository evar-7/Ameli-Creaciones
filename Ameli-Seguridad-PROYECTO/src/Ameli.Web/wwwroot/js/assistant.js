"use strict";
window.ameliShop = (() => {
  const el = (tag, text, cls) => { const node = document.createElement(tag); if (text != null) node.textContent = text; if (cls) node.className = cls; return node; };
  const money = value => new Intl.NumberFormat("es-CR", { style: "currency", currency: "CRC" }).format(value);
  const date = value => new Date(value).toLocaleString("es-CR");
  async function request(path, method = "GET", body, signal) {
    const headers = { Accept: "application/json" };
    if (method !== "GET") headers["X-CSRF-TOKEN"] = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    if (body !== undefined) headers["Content-Type"] = "application/json";
    const response = await fetch(path, { method, body: body === undefined ? undefined : JSON.stringify(body), signal, headers, credentials: "same-origin", cache: "no-store" });
    if (response.status === 204) return null;
    let data; try { data = await response.json(); } catch { throw new Error("No se pudo leer la respuesta. Reintenta cuando haya conexión."); }
    if (!response.ok) throw Object.assign(new Error(data.message || (response.status === 401 ? "Inicia sesión para continuar." : "No se pudo completar la operación.")), { status: response.status, code: data.code, errors: data.errors });
    return data;
  }
  function bindCartForm(form) {
    if (form.dataset.cartBound) return;
    form.dataset.cartBound = "true";
    const button = form.querySelector('button[type="submit"]'), result = form.querySelector(".cart-add-result");
    const size = form.elements.namedItem("Size"), quantity = form.elements.namedItem("Quantity"), acknowledgment = form.elements.namedItem("AcknowledgesUnspecifiedSize");
    const controls = [size, quantity, acknowledgment].filter(Boolean).map(input => ({ input, disabled: input.disabled }));
    const lock = value => controls.forEach(control => control.input.disabled = value || control.disabled);
    let command, pending = false;
    form.addEventListener("submit", async event => {
      event.preventDefault();
      if (pending || (!command && !form.reportValidity())) return;
      command ||= {
        requestId: crypto.randomUUID(), productId: form.dataset.productId,
        petId: form.dataset.petId || null, size: size?.value || "", quantity: Number(quantity.value),
        acknowledgesUnspecifiedSize: acknowledgment?.checked === true
      };
      pending = true; button.disabled = true; lock(true); result.textContent = "Agregando al carrito…";
      try {
        const data = await request("/account/shop/cart", "POST", command);
        result.textContent = data.message;
        const link = el("a", "Ir al carrito"); link.href = "/carrito"; result.append(link);
        command = null; lock(false); button.textContent = "Agregar más";
      } catch (error) {
        result.textContent = error.message;
        button.textContent = "Reintentar agregar";
        // A missing response or HTTP 5xx can follow a committed write. Retry the same selection and ID.
        if (error.status >= 400 && error.status < 500) { command = null; lock(false); }
      } finally { pending = false; button.disabled = false; }
    });
  }
  function createCartForm(product, selection = {}) {
    if (selection.petId && !product.sizes.some(size => size.name === selection.size))
      return el("p", "Actualiza las recomendaciones para obtener una talla compatible.");
    const form = el("form", null, "cart-add-form formulario-apilado");
    form.dataset.productId = product.id;
    if (selection.petId) form.dataset.petId = selection.petId;
    if (product.sizes.length) {
      const label = el("label", "Talla "), select = el("select"); select.name = "Size"; select.required = true;
      select.setAttribute("aria-label", `Talla de ${product.name}`);
      const placeholder = el("option", "Seleccionar talla"); placeholder.value = ""; select.append(placeholder);
      for (const size of product.sizes) { const option = el("option", size.name); option.value = size.name; select.append(option); }
      if (selection.size) select.value = selection.size;
      select.disabled = Boolean(selection.petId); label.append(select); form.append(label);
    } else {
      form.append(el("p", "Este producto no tiene tallas publicadas. No se puede verificar su ajuste."));
      const label = el("label", null, "cart-size-confirmation"), acknowledgment = el("input");
      acknowledgment.type = "checkbox"; acknowledgment.name = "AcknowledgesUnspecifiedSize"; acknowledgment.required = true;
      label.append(acknowledgment, document.createTextNode("Quiero comprarlo sin talla especificada.")); form.append(label);
    }
    const label = el("label", "Cantidad "), quantity = el("input"); quantity.type = "number"; quantity.name = "Quantity";
    quantity.min = "1"; quantity.max = String(Math.min(20, product.stockQuantity)); quantity.step = "1"; quantity.value = "1"; quantity.required = true;
    quantity.setAttribute("aria-label", `Cantidad de ${product.name}`); label.append(quantity);
    const add = el("button", "Agregar al carrito", "boton primario"); add.type = "submit";
    const result = el("p", null, "cart-add-result"); result.setAttribute("role", "status"); result.setAttribute("aria-live", "polite");
    form.append(label, add, result); bindCartForm(form); return form;
  }
  function sizesTable(sizes) {
    const wrap = el("div", null, "chat-table"), table = el("table");
    table.append(el("caption", "Equivalencias de tallas del producto"));
    const head = el("thead"), row = el("tr");
    for (const title of ["Talla", "Altura (cm)", "Peso (kg)", "Equivalencia"]) { const th = el("th", title); th.scope = "col"; row.append(th); }
    head.append(row); table.append(head); const body = el("tbody");
    for (const size of sizes) { const r = el("tr"); r.append(el("td", size.name), el("td", `${size.minHeightCm}–${size.maxHeightCm}`), el("td", `${size.minWeightKg}–${size.maxWeightKg}`), el("td", size.equivalent || "Sin equivalencia publicada")); body.append(r); }
    table.append(body); wrap.append(table); return wrap;
  }
  function productCard(card, isClient) {
    const p = card.product, node = el("article", null, "chat-product");
    node.append(el("h3", `${p.icon || ""} ${p.name}`), el("p", `${p.sku} · ${money(p.unitPrice)}`),
      el("p", p.stockQuantity > 0 ? `${p.stockQuantity} disponibles` : "Agotado"),
      el("p", `Tallas: ${p.sizes.map(s => s.name).join(", ") || "Sin tabla publicada"}`),
      el("p", `Materiales: ${p.material || "Sin información publicada"}`), el("p", `Cuidados: ${p.careInstructions || "Sin información publicada"}`),
      el("p", `Última actualización del producto: ${date(p.updatedAtUtc)}`));
    if (card.suggestedSize) node.append(el("strong", `Talla sugerida: ${card.suggestedSize}`));
    if (card.reason) node.append(el("p", card.reason));
    const link = el("a", "Ver producto", "boton secundario"); link.href = p.productUrl;
    const guide = el("a", "Guía visual", "boton claro"); guide.href = p.guideUrl; node.append(link, guide);
    if (p.stockQuantity > 0) {
      if (!isClient) { const login = el("a", "Iniciar sesión para agregar", "boton claro"); login.href = "/ingresar"; node.append(login); }
      else node.append(createCartForm(p, { size: card.suggestedSize, petId: card.petId }));
    }
    return node;
  }
  return { el, request, money, date, sizesTable, productCard, bindCartForm, createCartForm };
})();

document.addEventListener("DOMContentLoaded", () => {
  const root = document.getElementById("ameli-chat"); if (!root) return;
  const { el, request, productCard, sizesTable, date } = window.ameliShop;
  const launch = document.getElementById("chat-launch"), close = document.getElementById("chat-close"), tools = document.getElementById("chat-tools");
  const form = document.getElementById("chat-send"), input = document.getElementById("chat-message"), log = document.getElementById("chat-log"), status = document.getElementById("chat-status");
  const send = document.getElementById("chat-send-button"), cancel = document.getElementById("chat-cancel"), retry = document.getElementById("chat-retry");
  const intent = document.getElementById("chat-intent"), products = document.getElementById("chat-product"), pets = document.getElementById("chat-pet");
  const storageKey = "ameli-chat-draft:" + root.dataset.scope;
  const isClient = root.dataset.client === "true";
  let active = null, retained = null, initialized = false;
  const shown = new Set(), bubbles = new Map();
  const scroll = () => { root.querySelector(".chat-body").scrollTop = root.querySelector(".chat-body").scrollHeight; };
  const persist = payload => { try { if (payload) sessionStorage.setItem(storageKey, JSON.stringify(payload)); else sessionStorage.removeItem(storageKey); } catch { /* The in-memory draft remains usable. */ } };
  const bubble = (text, author = "Ameli") => { const node = el("article", null, "chat-bubble" + (author === "Tú" ? " user" : "")); node.append(el("strong", author, "chat-author"), el("p", text)); log.append(node); scroll(); return node; };
  bubble("Hola. Puedo consultar productos y tallas, recomendar opciones para tus mascotas y revisar tus pedidos cuando inicies sesión.");
  function renderReply(reply) {
    if (shown.has(reply.requestId)) return; shown.add(reply.requestId);
    const node = bubble(reply.message);
    if (reply.missingFields.length) { const list = el("ul"); for (const item of reply.missingFields) list.append(el("li", item.label)); node.append(list); }
    if (reply.order) {
      node.append(el("p", `Última actualización: ${date(reply.order.updatedAtUtc)}`),
        el("p", `Transportista: ${reply.order.carrier || "Todavía no asignado"}`), el("p", `Guía: ${reply.order.trackingNumber || "Todavía no asignada"}`));
    }
    for (const card of reply.products) node.append(productCard(card, isClient));
    if (reply.size?.table.length) node.append(sizesTable(reply.size.table));
    if (reply.size?.fitNotes) node.append(el("p", `Ajuste: ${reply.size.fitNotes}`));
    if (reply.actionUrl) { const link = el("a", reply.actionLabel || "Continuar", "boton secundario"); link.href = reply.actionUrl; node.append(link); }
    scroll();
  }
  function markRetained(payload, message) {
    retained = payload; persist(payload); status.textContent = message; retry.hidden = false;
    const node = bubbles.get(payload.requestId); if (node) node.querySelector(".chat-delivery").textContent = "Pendiente de respuesta";
  }
  async function submit(payload) {
    if (active) return;
    if (!bubbles.has(payload.requestId)) { const node = bubble(payload.message || ({ catalog: "Consultar producto", size: "Calcular talla", order: `Consultar pedido ${payload.orderNumber}`, recommendations: "Recomendar para mi mascota" }[payload.intent]), "Tú"); node.append(el("small", "Procesando", "chat-delivery")); bubbles.set(payload.requestId, node); }
    if (!navigator.onLine) { markRetained(payload, "Sin conexión: la consulta no fue enviada. Conservé el texto para que puedas reintentar."); return; }
    const own = { id: payload.requestId, controller: new AbortController() }; active = own; send.disabled = true; retry.hidden = true; cancel.hidden = true;
    retained = payload; persist(payload); status.textContent = "Procesando tu consulta…";
    const timer = setTimeout(() => { if (active === own) { status.textContent = "Sigo procesando tu consulta. Puedes esperar o cancelar."; cancel.hidden = false; } }, 4000);
    try {
      const reply = await request("/account/assistant/messages", "POST", payload, own.controller.signal);
      if (active !== own) return;
      renderReply(reply); bubbles.get(payload.requestId).querySelector(".chat-delivery").textContent = "Respondido";
      retained = null; persist(null); input.value = ""; status.textContent = "Consulta respondida.";
    } catch (error) {
      if (active !== own) return;
      if (error.name !== "AbortError") markRetained(payload, navigator.onLine ? (error.message || "No se recibió una respuesta. Puedes reintentar.") : "Sin conexión. Conservé la consulta para reintentar.");
    } finally {
      clearTimeout(timer);
      if (active === own) { active = null; send.disabled = false; cancel.hidden = true; }
    }
  }
  function payload() {
    const body = { requestId: crypto.randomUUID(), message: input.value.trim(), intent: intent.value };
    if (["catalog", "size"].includes(body.intent) && products.value) body.productId = products.value;
    if (body.intent === "size") for (const name of ["Species", "Breed", "HeightCm", "WeightKg"]) body[name[0].toLowerCase() + name.slice(1)] = tools.elements.namedItem(name).value.trim();
    if (body.intent === "order") body.orderNumber = tools.elements.namedItem("OrderNumber").value.trim();
    if (body.intent === "recommendations" && pets.value) body.petId = pets.value;
    return body;
  }
  form.addEventListener("submit", event => { event.preventDefault(); const body = payload(); if (!body.message && body.intent === "auto") { status.textContent = "Escribe una pregunta o elige una función."; input.focus(); return; } void submit(body); });
  input.addEventListener("keydown", event => { if (event.key === "Enter" && !event.shiftKey && !event.isComposing) { event.preventDefault(); form.requestSubmit(); } });
  input.addEventListener("input", () => { if (!active && !retained) persist({ message: input.value, intent: "auto", requestId: crypto.randomUUID() }); });
  intent.addEventListener("change", () => { root.querySelectorAll("[data-chat-tool]").forEach(node => { node.hidden = !node.dataset.chatTool.split(" ").includes(intent.value); }); });
  async function loadProducts(search = "") {
    const data = await request("/account/assistant/products?search=" + encodeURIComponent(search));
    products.replaceChildren(el("option", "Seleccionar producto")); products.firstChild.value = "";
    for (const p of data) { const option = el("option", `${p.name} · ${p.sku}`); option.value = p.id; products.append(option); }
    if (search && data.length === 1) products.value = data[0].id;
    if (search && !data.length) status.textContent = "No encontré un producto activo con ese nombre o SKU.";
  }
  document.getElementById("chat-find-product").addEventListener("click", () => { void loadProducts(document.getElementById("chat-product-search").value).catch(error => { status.textContent = error.message; }); });
  tools.addEventListener("submit", event => { event.preventDefault(); document.getElementById("chat-find-product").click(); });
  launch.addEventListener("click", async () => {
    root.hidden = false; launch.hidden = true; launch.setAttribute("aria-expanded", "true"); input.focus();
    if (initialized) return; initialized = true;
    try { await loadProducts(); const list = await request("/account/assistant/pets"); for (const pet of list) { const option = el("option", pet.name + (pet.isActive ? "" : " · Inactiva")); option.value = pet.id; pets.append(option); } }
    catch (error) { status.textContent = "No se pudieron cargar las opciones. Puedes escribir una pregunta o reabrir el chat para intentar de nuevo."; initialized = false; }
  });
  const hide = () => { root.hidden = true; launch.hidden = false; launch.setAttribute("aria-expanded", "false"); launch.focus(); };
  close.addEventListener("click", hide); root.addEventListener("keydown", event => { if (event.key === "Escape") hide(); });
  document.getElementById("chat-font").addEventListener("click", () => root.classList.toggle("large-text"));
  cancel.addEventListener("click", () => {
    if (!active) return; const old = active; active = null; old.controller.abort(); send.disabled = false; cancel.hidden = true;
    if (retained) markRetained(retained, "Consulta cancelada. El texto está guardado por si quieres reintentar.");
  });
  retry.addEventListener("click", () => { if (retained) void submit(retained); });
  window.addEventListener("offline", () => { if (active) { active.controller.abort(); active = null; send.disabled = false; cancel.hidden = true; } if (retained) markRetained(retained, "Sin conexión. Conservé tu consulta para reintentar."); });
  window.addEventListener("online", () => { if (retained) status.textContent = "La conexión volvió. Pulsa Reintentar para enviar la consulta guardada."; });
  try { const draft = JSON.parse(sessionStorage.getItem(storageKey) || "null"); if (draft?.requestId && typeof draft.message === "string") { retained = draft; input.value = draft.message; intent.value = draft.intent || "auto"; intent.dispatchEvent(new Event("change")); retry.hidden = false; status.textContent = "Tienes una consulta guardada. Puedes reintentar."; } } catch { persist(null); }
  document.querySelectorAll('form[action="/account/logout"]').forEach(node => node.addEventListener("submit", () => persist(null)));
});
