"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const root = document.getElementById("management"); if (!root) return;
  const base = "/account/management/", feedback = document.getElementById("management-message");
  const csrf = root.querySelector('[name="__RequestVerificationToken"]').value;
  const el = (tag, text, cls) => { const e = document.createElement(tag); if (text != null) e.textContent = text; if (cls) e.className = cls; return e; };
  const show = (text, ok = false) => { feedback.replaceChildren(el("span", text)); feedback.className = "feedback " + (ok ? "success" : "error"); };
  const date = value => value ? new Date(value).toLocaleString("es-CR", { timeZone: "UTC" }) : "—";
  async function request(path, body, signal) {
    const response = await fetch(base + path, { method: body === undefined ? "GET" : "POST", credentials: "same-origin", cache: "no-store", signal,
      headers: body === undefined ? {} : { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (response.status === 401) throw { message: "La sesión expiró. Vuelve a iniciar sesión." };
    if (!response.ok) { let error; try { error = await response.json(); } catch { error = {}; } throw { ...error, message: error.message || "No se pudo completar la operación." }; }
    return response;
  }
  root.querySelectorAll("[data-cancel-dialog]").forEach(b => b.addEventListener("click", () => b.closest("dialog").close()));
  if (root.dataset.mode === "editor") {
    const form = document.getElementById("account-editor"), field = n => form.elements.namedItem(n);
    const save = document.getElementById("save-account"), dialog = document.getElementById("state-confirm"), conflict = document.getElementById("account-conflict");
    const id = root.dataset.id; let originalState = true, current, confirmed = false;
    const fill = u => { for (const n of ["Name", "Email", "Phone", "Role", "Revision"]) field(n).value = u[n[0].toLowerCase() + n.slice(1)] || ""; field("IsActive").value = String(u.isActive); originalState = u.isActive; if (field("Reason")) field("Reason").value = ""; if (field("Password")) field("Password").value = ""; if (field("ConfirmPassword")) field("ConfirmPassword").value = ""; };
    const clear = () => { form.querySelectorAll("[data-error]").forEach(e => e.textContent = ""); form.querySelectorAll("[aria-invalid]").forEach(e => e.removeAttribute("aria-invalid")); feedback.classList.add("oculto"); };
    const errors = e => {
      show(e.message || "No se pudo guardar la cuenta.");
      for (const [key, values] of Object.entries(e.errors || {})) {
        const name = key.split(".").pop().toLowerCase(), span = [...form.querySelectorAll("[data-error]")].find(s => s.dataset.error.toLowerCase() === name);
        if (span) { span.textContent = values.join(" "); field(span.dataset.error).setAttribute("aria-invalid", "true"); }
      }
      if (e.existingId) { const a = el("a", " Abrir cuenta existente"); a.href = "/administracion/usuarios/" + encodeURIComponent(e.existingId); feedback.append(a); }
      if (e.current) { current = e.current; conflict.classList.remove("oculto"); document.getElementById("current-account").textContent = `Nombre: ${current.name}\nCorreo: ${current.email}\nTeléfono: ${current.phone}\nRol: ${current.role}\nEstado: ${current.isActive ? "Activo" : "Inactivo"}\nModificada (UTC): ${date(current.updatedAtUtc)}`; }
      feedback.focus();
    };
    if (id) { save.disabled = true; request("accounts/" + id).then(r => r.json()).then(u => { fill(u); save.disabled = false; }).catch(errors); }
    form.addEventListener("submit", async e => {
      e.preventDefault(); if (save.disabled) return; clear();
      const body = { name: field("Name").value, email: field("Email").value, phone: field("Phone").value, role: field("Role").value,
        isActive: field("IsActive").value === "true", revision: field("Revision").value || null, reason: field("Reason")?.value || "", confirmed };
      const password = field("Password")?.value || "", confirmPassword = field("ConfirmPassword")?.value || "";
      if (!id || password || confirmPassword) {
        body.password = password; body.confirmPassword = confirmPassword;
        const strong = password.length >= 8 && password.length <= 64 && /\p{Lu}/u.test(password) && /\p{Ll}/u.test(password) && /\p{Nd}/u.test(password) && /[^\p{L}\p{N}\s]/u.test(password);
        if (!strong || password !== confirmPassword) {
          const passwordErrors = {};
          if (!strong) passwordErrors.Password = ["Usa de 8 a 64 caracteres, mayúscula, minúscula, número y símbolo."];
          if (password !== confirmPassword) passwordErrors.ConfirmPassword = ["Las contraseñas deben coincidir."];
          errors({ message: id ? "Revisa la nueva contraseña." : "Revisa la contraseña inicial.", errors: passwordErrors }); return;
        }
      }
      if (id && body.isActive !== originalState && !confirmed) {
        if (body.reason.trim().length < 5) { errors({ message: "Indica el motivo del cambio de estado.", errors: { Reason: ["Escribe al menos 5 caracteres."] } }); return; }
        dialog.showModal(); return;
      }
      save.disabled = true;
      try { const u = await (await request("accounts" + (id ? "/" + id : ""), body)).json();
        if (!id) { window.location.assign("/administracion/usuarios/" + u.id); return; }
        fill(u); conflict.classList.add("oculto"); current = null; show("Los cambios se guardaron correctamente.", true); feedback.focus();
      } catch (error) { errors(error); } finally { confirmed = false; save.disabled = false; }
    });
    document.getElementById("confirm-state").addEventListener("click", () => { confirmed = true; dialog.close(); form.requestSubmit(); });
    dialog.addEventListener("cancel", () => confirmed = false);
    document.getElementById("load-current").addEventListener("click", () => { if (!current) return; fill(current); current = null; confirmed = false; clear(); conflict.classList.add("oculto"); show("Versión vigente cargada. Revisa los datos y vuelve a aplicar tus cambios.", true); });
    return;
  }
  const audit = root.dataset.mode === "audit", filters = document.getElementById("management-filters"), results = document.getElementById("management-results");
  const initial = new URLSearchParams(location.search); [...filters.elements].forEach(e => { if (e.name && initial.has(e.name)) e.value = initial.get(e.name); });
  const values = () => Object.fromEntries([...new FormData(filters)].filter(([,v]) => v.toString().trim()));
  let pending;
  async function load(page = 1) {
    pending?.abort(); pending = new AbortController(); const own = pending;
    results.setAttribute("aria-busy", "true"); results.replaceChildren(el("p", "Consultando…", "estado-vacio"));
    try {
      const params = new URLSearchParams({ ...values(), page });
      const data = await (await request((audit ? "audit" : "accounts") + "?" + params, undefined, own.signal)).json();
      results.replaceChildren(el("div", `${data.total} ${audit ? "registros" : "cuentas internas"} · Fechas en UTC`, "result-heading"));
      history.replaceState(null, "", location.pathname + "?" + params);
      if (!data.items.length) { const empty = el("div", null, "estado-vacio"); empty.append(el("h2", "No se encontraron resultados"), el("p", "Ajusta la búsqueda o limpia los filtros.")); results.append(empty); }
      else {
        const wrap = el("div", null, "tabla-responsive"), table = el("table", null, audit ? "audit-table" : "");
        const headings = audit ? ["Fecha y hora (UTC)", "Actor y rol", "Módulo / Acción", "Resultado", "IP / Detalle"] : ["Nombre y correo", "Teléfono", "Rol", "Estado", "Última modificación", "Acciones"];
        const head = el("thead"), tr = el("tr"); headings.forEach(h => tr.append(el("th", h))); head.append(tr); table.append(head);
        const body = el("tbody");
        for (const u of data.items) {
          const row = el("tr"); const cell = text => { const td = el("td", text); row.append(td); return td; };
          if (audit) {
            cell(date(u.occurredAtUtc)).append(el("small", "Registro #" + u.id, "table-subtitle"));
            cell(u.actorName || "Sin identificación").append(el("small", u.actorEmail, "table-subtitle"), el("small", u.actorRole, "table-subtitle"));
            cell(u.module).append(el("code", u.action, "table-subtitle")); cell(u.outcome);
            const details = el("details"); details.append(el("summary", "Ver detalle"), el("pre", JSON.stringify(u, null, 2))); cell(u.origin).append(details);
          } else {
            cell(u.name).append(el("small", u.email, "table-subtitle")); cell(u.phone || "Por completar"); cell(u.role);
            cell().append(el("span", u.isActive ? "Activo" : "Inactivo", "estado " + (u.isActive ? "exito" : "peligro"))); cell(date(u.updatedAtUtc));
            const link = el("a", "Editar cuenta", "boton secundario"); link.href = "/administracion/usuarios/" + encodeURIComponent(u.id); cell().append(link);
          }
          body.append(row);
        }
        table.append(body); wrap.append(table); results.append(wrap);
      }
      const nav = el("nav", null, "pagination"); nav.setAttribute("aria-label", "Páginas de resultados"); nav.append(el("span", `Página ${data.page} de ${data.totalPages}`));
      for (const [label, target] of [["← Anterior", data.page - 1], ["Siguiente →", data.page + 1]]) if (target > 0 && target <= data.totalPages) { const b = el("button", label, "boton secundario"); b.type = "button"; b.addEventListener("click", () => void load(target)); nav.append(b); }
      results.append(nav);
    } catch (error) { if (error.name !== "AbortError") { results.replaceChildren(el("p", error.message || "No se pudo consultar.", "feedback error")); } }
    finally { if (own === pending) results.removeAttribute("aria-busy"); }
  }
  filters.addEventListener("submit", e => { e.preventDefault(); void load(); });
  document.getElementById("clear-filters").addEventListener("click", () => { filters.reset(); void load(); });
  void load(Math.max(1, Number(initial.get("page")) || 1));
  if (audit) {
    const dialog = document.getElementById("export-confirm"); let selected;
    document.getElementById("export-audit").addEventListener("click", () => { selected = values(); document.getElementById("export-filters").textContent = Object.entries(selected).map(([k,v]) => `${k}: ${v}`).join("\n") || "Todos los registros, sin filtros."; dialog.showModal(); });
    document.getElementById("confirm-export").addEventListener("click", async e => {
      const button = e.currentTarget; button.disabled = true;
      try { const response = await request("audit/export", { ...selected, confirmed: true }); const url = URL.createObjectURL(await response.blob()); const a = el("a"); a.href = url; a.download = "Ameli-Auditoria.zip"; a.click(); setTimeout(() => URL.revokeObjectURL(url), 10000); show("Exportación preparada con manifiesto y huella de integridad.", true); }
      catch (error) { show(error.message || "No se pudo exportar."); } finally { button.disabled = false; dialog.close(); feedback.focus(); }
    });
    document.getElementById("verify-audit").addEventListener("click", async e => {
      const button = e.currentTarget; button.disabled = true; show("Verificando la cadena completa…", true);
      try { const report = await (await request("audit/verify", values())).json(); show(`${report.message} Registros en los filtros: ${report.selectedRecords}. Cadena revisada: ${report.checkedRecords}. Inconsistencias: ${report.issueCount}.`, report.isValid);
        if (report.issues.length) { const list = el("ul"); report.issues.forEach(i => list.append(el("li", `Registro ${i.eventId ?? "cabecera"}: ${i.reason}`))); feedback.append(list); }
      } catch (error) { show(error.message || "No se pudo verificar."); } finally { button.disabled = false; feedback.focus(); }
    });
  }
});
