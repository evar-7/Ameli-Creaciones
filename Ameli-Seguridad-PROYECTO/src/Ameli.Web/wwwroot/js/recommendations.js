"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const root = document.getElementById("recommendations");
  if (!root) return;
  const pet = document.getElementById("recommendation-pet");
  const refresh = document.getElementById("recommendation-refresh");
  const feedback = document.getElementById("recommendation-feedback");
  const results = document.getElementById("recommendation-results");
  const csrf = document.getElementById("recommendation-token")?.querySelector('[name="__RequestVerificationToken"]')?.value || "";
  const sessionHideKey = "ameli-recommendation-hidden";
  const hidden = new Set(JSON.parse(sessionStorage.getItem(sessionHideKey) || "[]"));
  const money = value => new Intl.NumberFormat("es-CR", { style: "currency", currency: "CRC", maximumFractionDigits: 0 }).format(value || 0);
  const el = (tag, text, cls) => { const node = document.createElement(tag); if (text != null) node.textContent = text; if (cls) node.className = cls; return node; };
  const show = (text, ok = false) => { feedback.replaceChildren(el("span", text)); feedback.className = "feedback " + (ok ? "success" : "error"); feedback.focus(); };
  const clearMessage = () => feedback.className = "feedback oculto";
  const persistHidden = () => sessionStorage.setItem(sessionHideKey, JSON.stringify([...hidden]));
  async function call(path, method = "GET", body) {
    const headers = { Accept: "application/json" };
    if (method !== "GET") headers["X-CSRF-TOKEN"] = csrf;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    const response = await fetch(path, { method, credentials: "same-origin", cache: "no-store", headers, body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.ok) {
      let error = {};
      try { error = await response.json(); } catch { }
      const e = new Error(error.message || (response.status === 401 ? "Inicia sesión para guardar retroalimentación." : "No se pudo completar la operación."));
      e.code = error.code; e.errors = error.errors; e.status = response.status;
      throw e;
    }
    return response.json();
  }
  function renderAlternatives(alternatives, target) {
    if (!alternatives?.length) return;
    const title = el("h4", "Alternativas compatibles con stock");
    const list = el("ul");
    alternatives.forEach(item => list.append(el("li", `${item.name} · Talla ${item.suggestedSize} · ${money(item.unitPrice)} · ${item.stockQuantity} disponibles`)));
    target.append(title, list);
  }
  function empty(title, message, actionUrl, actionLabel, alternatives) {
    results.replaceChildren();
    const box = el("section", null, "panel estado-vacio");
    box.append(el("h2", title), el("p", message));
    renderAlternatives(alternatives, box);
    if (actionUrl && actionLabel) { const a = el("a", actionLabel, "boton secundario"); a.href = actionUrl; box.append(a); }
    results.append(box);
  }
  function renderIncomplete(data) {
    empty("Perfil incompleto", data.message, data.actionUrl, data.actionLabel);
    if (data.missingFields?.length) {
      const list = el("ul");
      data.missingFields.forEach(item => list.append(el("li", item.label)));
      results.querySelector("section")?.append(list);
    }
  }
  async function sendFeedback(item, value, reasonCode = "", reasonText = "") {
    const payload = { petId: pet.value, productId: item.productId, value, reasonCode, reasonText };
    const response = await call("/account/recommendations/feedback", "POST", payload);
    return response;
  }
  async function explain(item, target) {
    target.replaceChildren(el("p", "Cargando explicación…"));
    try {
      const params = new URLSearchParams({ petId: pet.value, productId: item.productId, contextToken: item.contextToken });
      const detail = await call("/account/recommendations/explanation?" + params.toString());
      target.replaceChildren();
      target.append(el("strong", detail.summary));
      if (detail.isTemporarilyUnavailable) {
        const retry = el("button", "Reintentar", "boton secundario"); retry.type = "button";
        retry.addEventListener("click", () => void explain(item, target));
        target.append(retry);
        return;
      }
      if (detail.isStale || detail.isUnavailable) {
        renderAlternatives(detail.alternatives, target);
        const link = el("a", detail.recalculateLabel || "Recalcular", "boton secundario");
        link.href = detail.recalculateUrl || "/recomendados";
        target.append(link);
        return;
      }
      const factors = el("ul");
      (detail.factors || []).forEach(f => factors.append(el("li", f)));
      target.append(factors);
      if (detail.confidence === "Baja") target.append(el("p", "Confianza baja: completa datos opcionales para mejorar la precisión."));
      if (detail.missingOptionalData?.length) target.append(el("p", "Datos sugeridos: " + detail.missingOptionalData.join(", ")));
    } catch (error) {
      target.replaceChildren(el("p", error.message || "La explicación no está disponible temporalmente."));
      const retry = el("button", "Reintentar", "boton secundario"); retry.type = "button";
      retry.addEventListener("click", () => void explain(item, target));
      target.append(retry);
    }
  }
  function card(item) {
    const wrap = el("div", null, "recomendacion-item");
    const article = el("article", null, "tarjeta-producto");
    const image = el("div", null, "imagen-producto"); image.append(el("span", item.icon || "🎀"));
    const content = el("div", null, "contenido-producto");
    content.append(el("span", item.categoryName, "categoria-producto"), el("h3", item.name));
    const foot = el("div", null, "pie-producto"); foot.append(el("strong", money(item.unitPrice)), el("span", `${item.stockQuantity} disponibles`));
    content.append(foot, el("p", `Talla sugerida: ${item.suggestedSize}`), el("p", `Confianza: ${item.confidence}`), el("p", item.reason));
    const actions = el("div", null, "grupo-botones");
    const product = el("a", "Ver producto", "boton primario"); product.href = "/productos/" + encodeURIComponent(item.productId);
    if (!item.canAddToCart || !item.isAvailable) { product.className = "boton secundario"; product.textContent = "No disponible"; }
    const why = el("button", "Por qué se recomienda", "boton claro"); why.type = "button";
    const useful = el("button", "Útil", "boton secundario"); useful.type = "button";
    const skip = el("button", "No me interesa", "boton claro"); skip.type = "button";
    actions.append(product, why, useful, skip); content.append(actions);
    article.append(image, content); wrap.append(article);
    const detail = el("div", null, "explicacion oculto"); wrap.append(detail);
    why.addEventListener("click", () => {
      detail.classList.toggle("oculto");
      if (!detail.dataset.loaded || detail.dataset.loaded !== item.contextToken) {
        detail.dataset.loaded = item.contextToken;
        void explain(item, detail);
      }
    });
    useful.addEventListener("click", async () => {
      useful.disabled = true;
      try {
        await sendFeedback(item, "useful");
        show("Retroalimentación guardada. Priorizaremos opciones similares.", true);
      } catch (error) { show(error.message || "No se pudo guardar la retroalimentación."); }
      finally { useful.disabled = false; }
    });
    skip.addEventListener("click", async () => {
      const reasonCode = prompt("Motivo (style, color, material, price u otro):", "style") || "other";
      const reasonText = prompt("Detalle opcional:", "") || "";
      skip.disabled = true;
      try {
        await sendFeedback(item, "not_interested", reasonCode.trim().toLowerCase(), reasonText.trim());
        hidden.add(item.productId); persistHidden();
        wrap.remove();
        show("Preferencia guardada. La sugerencia se ocultó en esta sesión.", true);
      } catch (error) { show(error.message || "No se pudo guardar la retroalimentación."); }
      finally { skip.disabled = false; }
    });
    return wrap;
  }
  async function load(announce = false) {
    clearMessage();
    if (!pet.value) { empty("Selecciona una mascota", "Primero elige una mascota activa para generar sugerencias.", "/perfil/mascotas", "Mis mascotas"); return; }
    refresh.disabled = true;
    try {
      const data = await call("/account/recommendations/list?petId=" + encodeURIComponent(pet.value));
      if (data.status === "incomplete_pet") { renderIncomplete(data); if (announce) show(data.message); return; }
      if (data.status === "fallback") {
        empty("Cálculo temporalmente no disponible", data.message, data.actionUrl, data.actionLabel, data.alternatives);
        if (announce) show(data.message);
        return;
      }
      if (data.status !== "ok" || !data.items?.length) {
        empty("Sin productos compatibles", data.message || "No se encontraron coincidencias seguras.", data.actionUrl, data.actionLabel, data.alternatives);
        if (announce) show(data.message || "No hay recomendaciones compatibles.");
        return;
      }
      results.replaceChildren();
      data.items.filter(item => !hidden.has(item.productId)).forEach(item => results.append(card(item)));
      if (announce) show("Recomendaciones actualizadas.", true);
    } catch (error) {
      empty("No disponible", error.message || "No se pudieron cargar las recomendaciones.", "/perfil/mascotas", "Ver perfil");
      show(error.message || "No se pudieron cargar las recomendaciones.");
    } finally {
      refresh.disabled = false;
    }
  }
  async function loadPets() {
    const items = await call("/account/recommendations/pets");
    items.forEach(p => {
      const option = el("option", `${p.name} · ${p.species}`);
      option.value = p.id;
      pet.append(option);
    });
    if (items.length) { pet.value = items[0].id; await load(false); }
    else empty("Sin mascotas activas", "Registra al menos una mascota para usar recomendaciones personalizadas.", "/perfil/mascotas", "Registrar mascota");
  }
  refresh.addEventListener("click", () => void load(true));
  pet.addEventListener("change", () => void load(false));
  loadPets().catch(error => {
    empty("No disponible", error.message || "No se pudo cargar la información inicial.", "/perfil/mascotas", "Mis mascotas");
  });
});
