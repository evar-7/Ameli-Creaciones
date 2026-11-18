"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const { request } = window.ameliShop;
  for (const root of document.querySelectorAll("[data-availability-kind]")) {
    const form = root.querySelector("form"), result = root.querySelector("[data-availability-result]");
    const action = root.querySelector("[data-availability-action]"), reason = form.elements.namedItem("Reason");
    const path = `/account/catalogs/${root.dataset.availabilityKind}/${root.dataset.id}`;
    let current;
    async function load() {
      action.disabled = true; const data = await request(path); current = { ...(data.product || data), revision: data.revision };
      root.querySelector("[data-availability-state]").textContent = current.isActive ? "Estado actual: Activo" : "Estado actual: Inactivo";
      root.querySelector("[data-availability-reason]").classList.toggle("oculto", !current.isActive);
      reason.required = current.isActive; action.textContent = current.isActive ? "Desactivar" : "Reactivar"; action.disabled = false;
    }
    root.querySelector("[data-availability-reload]").addEventListener("click", () => void load().catch(error => handleIdentityError(form, result, error)));
    form.addEventListener("submit", async event => {
      event.preventDefault(); clearIdentityErrors(form, result);
      if (!current || !form.reportValidity()) return;
      if (current.isActive && !reason.value.trim()) { handleIdentityError(form, result, { message: "Indica el motivo de desactivación.", errors: { Reason: ["El motivo es obligatorio."] } }); return; }
      if (!confirm(`¿${current.isActive ? "Desactivar" : "Reactivar"} ${current.name}? Se recargarán los datos guardados del formulario.`)) return;
      action.disabled = true;
      try {
        await request(path + "/availability", "PUT", { isActive: !current.isActive, revision: current.revision, reason: reason.value.trim() });
        await load(); reason.value = ""; showIdentityMessage(result, "Disponibilidad actualizada correctamente.", "info");
        document.dispatchEvent(new CustomEvent("catalog-availability-changed"));
      } catch (error) { handleIdentityError(form, result, error); }
      finally { action.disabled = false; }
    });
    document.addEventListener("catalog-editor-saved", () => void load().catch(error => handleIdentityError(form, result, error)));
    void load().catch(error => handleIdentityError(form, result, error));
  }
  const root = document.getElementById("category-editor"); if (!root) return;
  const form = document.getElementById("category-edit-form"), result = document.getElementById("category-edit-result");
  const save = form.querySelector('button[type="submit"]'), path = `/account/catalogs/categories/${root.dataset.id}`;
  let current;
  async function load() {
    save.disabled = true; current = await request(path);
    form.elements.namedItem("Name").value = current.name; form.elements.namedItem("Description").value = current.description;
    save.disabled = false;
  }
  document.getElementById("category-edit-reload").addEventListener("click", () => {
    if (confirm("¿Reemplazar lo escrito con la versión vigente?")) void load().catch(error => handleIdentityError(form, result, error));
  });
  form.addEventListener("submit", async event => {
    event.preventDefault(); clearIdentityErrors(form, result); if (!current || !form.reportValidity()) return;
    save.disabled = true;
    try {
      current = await request(path, "PUT", { name: form.elements.namedItem("Name").value, description: form.elements.namedItem("Description").value, revision: current.revision });
      showIdentityMessage(result, "Categoría actualizada correctamente.", "info");
      document.dispatchEvent(new CustomEvent("catalog-editor-saved"));
    } catch (error) { handleIdentityError(form, result, error); }
    finally { save.disabled = false; }
  });
  document.addEventListener("catalog-availability-changed", () => void load().catch(error => handleIdentityError(form, result, error)));
  void load().catch(error => handleIdentityError(form, result, error));
});
