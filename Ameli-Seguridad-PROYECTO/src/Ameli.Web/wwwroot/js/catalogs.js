"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const root = document.getElementById("catalog-management"); if (!root) return;
  const kind = root.dataset.kind, base = "/account/catalogs/", feedback = document.getElementById("catalog-message");
  const csrf = document.getElementById("catalog-token")?.querySelector('[name="__RequestVerificationToken"]')?.value;
  const editor = document.getElementById("catalog-editor"), filters = document.getElementById("catalog-filters"), results = document.getElementById("catalog-results");
  const el = (tag, text, cls) => { const node = document.createElement(tag); if (text != null) node.textContent = text; if (cls) node.className = cls; return node; };
  const date = value => value ? new Date(value).toLocaleString("es-CR", { timeZone: "UTC" }) : "—";
  const money = value => new Intl.NumberFormat("es-CR", { style: "currency", currency: "CRC", maximumFractionDigits: 0 }).format(value || 0);
  const endpoint = kind;
  const clearFeedback = () => feedback.className = "feedback oculto";
  const show = (text, ok = false) => { feedback.replaceChildren(el("span", text)); feedback.className = "feedback " + (ok ? "success" : "error"); feedback.focus(); };
  async function request(path, body, signal) {
    const response = await fetch(base + path, { method: body === undefined ? "GET" : "POST", credentials: "same-origin", cache: "no-store", signal,
      headers: body === undefined ? {} : { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (response.status === 401) throw { message: "La sesión expiró. Vuelve a iniciar sesión." };
    if (!response.ok) { let error; try { error = await response.json(); } catch { error = {}; } throw { ...error, message: error.message || "No se pudo completar la operación." }; }
    return response;
  }
  const values = form => Object.fromEntries([...new FormData(form)].filter(([, value]) => value.toString().trim()));
  const clearErrors = () => {
    clearFeedback();
    editor.querySelectorAll("[data-error]").forEach(e => e.textContent = "");
    editor.querySelectorAll("[aria-invalid]").forEach(e => e.removeAttribute("aria-invalid"));
  };
  const applyErrors = error => {
    show(error.message || "No se pudo guardar.");
    for (const [key, messages] of Object.entries(error.errors || {})) {
      const name = key.split(".").pop().toLowerCase();
      const span = [...editor.querySelectorAll("[data-error]")].find(s => s.dataset.error.toLowerCase() === name);
      if (span) {
        span.textContent = messages.join(" ");
        const field = editor.elements.namedItem(span.dataset.error);
        field?.setAttribute("aria-invalid", "true");
      }
    }
  };
  async function loadLookups() {
    if (kind !== "products") return;
    const [categories, suppliers] = await Promise.all([
      (await request("categories?isActive=true&page=1&pageSize=100")).json(),
      (await request("suppliers?isActive=true&page=1&pageSize=100")).json()
    ]);
    const fill = (selector, items, placeholder) => {
      root.querySelectorAll(`select[data-lookup="${selector}"]`).forEach(select => {
        const value = select.value;
        select.replaceChildren(el("option", placeholder));
        select.firstElementChild.value = "";
        for (const item of items.items) {
          const option = el("option", item.name + (item.isActive ? "" : " · Inactivo"));
          option.value = item.id;
          select.append(option);
        }
        select.value = value;
      });
    };
    fill("categories", categories, root.dataset.kind === "products" ? "Seleccionar categoría" : "Todas las categorías");
    root.querySelectorAll('select[name="categoryId"]').forEach(select => { if (select.closest("#catalog-filters")) select.options[0].textContent = "Todas las categorías"; });
    fill("suppliers", suppliers, root.dataset.kind === "products" ? "Seleccionar proveedor" : "Todos los proveedores");
    root.querySelectorAll('select[name="supplierId"]').forEach(select => { if (select.closest("#catalog-filters")) select.options[0].textContent = "Todos los proveedores"; });
  }
  const renderEmpty = text => { const empty = el("div", null, "estado-vacio"); empty.append(el("h2", "Sin resultados"), el("p", text)); results.replaceChildren(empty); };
  const renderTable = (headings, build) => {
    const wrap = el("div", null, "tabla-responsive"), table = el("table");
    const thead = el("thead"), row = el("tr"); headings.forEach(h => row.append(el("th", h))); thead.append(row); table.append(thead);
    const tbody = el("tbody"); build(tbody); table.append(tbody); wrap.append(table); results.append(wrap);
  };
  function renderItems(data) {
    results.replaceChildren(el("div", `${data.total} registros`, "result-heading"));
    if (!data.items.length) { renderEmpty("Ajusta la búsqueda o limpia los filtros."); return; }
    if (kind === "products") {
      const grid = el("section", null, "productos-grid");
      for (const item of data.items) {
        const card = el("article", null, "tarjeta-producto");
        const image = el("div", null, "imagen-producto"); image.append(el("span", item.icon || "🎀"), el("small", item.species));
        const content = el("div", null, "contenido-producto");
        content.append(el("span", item.categoryName, "categoria-producto"), el("h3", item.name));
        const meta = el("p", `${item.supplierName}${item.material ? " · " + item.material : ""}`, "table-subtitle");
        const footer = el("div", null, "pie-producto"); footer.append(el("strong", money(item.unitPrice)), el("span", `${item.stockQuantity} disponibles`));
        const state = el("p", item.isActive ? "Activo" : "Inactivo", "estado " + (item.isActive ? "exito" : "peligro"));
        if (item.description) content.append(el("p", item.description));
        content.append(meta, footer, state);
        card.append(image, content); grid.append(card);
      }
      results.append(grid);
    } else if (kind === "categories") {
      renderTable(["Nombre", "Descripción", "Estado", "Última actualización (UTC)"], tbody => {
        for (const item of data.items) {
          const row = el("tr");
          row.append(el("td", item.name), el("td", item.description || "Sin descripción"));
          const state = el("span", item.isActive ? "Activo" : "Inactivo", "estado " + (item.isActive ? "exito" : "peligro"));
          const stateCell = el("td"); stateCell.append(state); row.append(stateCell, el("td", date(item.updatedAtUtc))); tbody.append(row);
        }
      });
    } else {
      renderTable(["Proveedor", "Contacto", "Correo", "Teléfono", "Estado"], tbody => {
        for (const item of data.items) {
          const row = el("tr");
          row.append(el("td", item.name), el("td", item.contactName || "Sin contacto"), el("td", item.email), el("td", item.phone));
          const state = el("span", item.isActive ? "Activo" : "Inactivo", "estado " + (item.isActive ? "exito" : "peligro"));
          const stateCell = el("td"); stateCell.append(state); row.append(stateCell); tbody.append(row);
        }
      });
    }
    const nav = el("nav", null, "pagination"); nav.setAttribute("aria-label", "Páginas de resultados"); nav.append(el("span", `Página ${data.page} de ${data.totalPages}`));
    for (const [label, target] of [["← Anterior", data.page - 1], ["Siguiente →", data.page + 1]]) if (target > 0 && target <= data.totalPages) {
      const button = el("button", label, "boton secundario"); button.type = "button"; button.addEventListener("click", () => void load(target)); nav.append(button);
    }
    results.append(nav);
  }
  let pending;
  async function load(page = 1) {
    pending?.abort(); pending = new AbortController(); const own = pending;
    results.setAttribute("aria-busy", "true"); results.replaceChildren(el("p", "Consultando…", "estado-vacio"));
    try {
      const params = new URLSearchParams({ ...values(filters), page });
      const data = await (await request(endpoint + "?" + params, undefined, own.signal)).json();
      history.replaceState(null, "", location.pathname + "?" + params);
      renderItems(data);
    } catch (error) {
      if (error.name !== "AbortError") results.replaceChildren(el("p", error.message || "No se pudo consultar.", "feedback error"));
    } finally {
      if (own === pending) results.removeAttribute("aria-busy");
    }
  }
  editor.addEventListener("submit", async event => {
    event.preventDefault();
    clearErrors();
    const raw = values(editor);
    const body = kind === "categories"
      ? { name: raw.Name || "", description: raw.Description || "", isActive: raw.IsActive !== "false" }
      : kind === "suppliers"
        ? { name: raw.Name || "", contactName: raw.ContactName || "", email: raw.Email || "", phone: raw.Phone || "", notes: raw.Notes || "", isActive: raw.IsActive !== "false" }
        : { name: raw.Name || "", description: raw.Description || "", categoryId: raw.CategoryId || null, supplierId: raw.SupplierId || null, species: raw.Species || "", material: raw.Material || "", icon: raw.Icon || "", unitPrice: Number(raw.UnitPrice || 0), stockQuantity: Number(raw.StockQuantity || 0), isActive: raw.IsActive !== "false" };
    const button = editor.querySelector('button[type="submit"]'); button.disabled = true;
    try {
      await (await request(endpoint, body)).json();
      editor.reset();
      await loadLookups();
      show("Registro guardado correctamente.", true);
      await load(1);
    } catch (error) {
      applyErrors(error);
    } finally {
      button.disabled = false;
    }
  });
  filters.addEventListener("submit", event => { event.preventDefault(); void load(); });
  document.getElementById("clear-catalog-filters")?.addEventListener("click", () => { filters.reset(); void load(1); });
  const initial = new URLSearchParams(location.search);
  for (const element of filters.elements) if (element.name && initial.has(element.name)) element.value = initial.get(element.name);
  Promise.resolve(loadLookups()).then(() => {
    for (const element of filters.elements) if (element.name && initial.has(element.name)) element.value = initial.get(element.name);
    void load(Math.max(1, Number(initial.get("page")) || 1));
  }).catch(error => results.replaceChildren(el("p", error.message || "No se pudieron cargar los catálogos auxiliares.", "feedback error")));
});
