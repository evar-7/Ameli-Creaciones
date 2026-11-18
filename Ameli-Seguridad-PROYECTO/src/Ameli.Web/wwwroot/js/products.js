"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const root = document.getElementById("product-editor");
  if (!root) return;
  const { el, request } = window.ameliShop;
  const form = document.getElementById("product-edit-form");
  const feedback = document.getElementById("product-edit-result");
  const sizes = document.getElementById("product-edit-sizes");
  const preview = document.getElementById("product-edit-preview");
  const reloadButton = document.getElementById("product-edit-reload");
  const save = form.querySelector('button[type="submit"]');
  const path = `/account/catalogs/products/${root.dataset.id}`;
  let current;

  function sizeRow(size = {}) {
    const row = el("div", null, "presentation-size-row");
    for (const [name, label] of [["Name", "Talla"], ["MinHeightCm", "Altura mín. cm"], ["MaxHeightCm", "Altura máx. cm"], ["MinWeightKg", "Peso mín. kg"], ["MaxWeightKg", "Peso máx. kg"], ["Equivalent", "Equivalencia"]]) {
      const input = el("input"), wrapper = el("label", label);
      input.dataset.name = name; input.value = size[name[0].toLowerCase() + name.slice(1)] ?? "";
      if (name.includes("Cm") || name.includes("Kg")) { input.type = "number"; input.min = "0.01"; input.step = "0.01"; input.required = true; }
      else { input.maxLength = name === "Name" ? 20 : 80; input.required = name === "Name"; }
      wrapper.append(input); row.append(wrapper);
    }
    const remove = el("button", "Quitar talla", "boton secundario"); remove.type = "button";
    remove.addEventListener("click", () => row.remove()); row.append(remove); sizes.append(row);
  }

  async function lookup(kind, selected) {
    const select = form.elements.namedItem(kind === "categories" ? "CategoryId" : "SupplierId");
    select.replaceChildren();
    let page = 1, totalPages = 1;
    do {
      const data = await request(`/account/catalogs/${kind}?page=${page}&pageSize=100`);
      totalPages = data.totalPages;
      for (const item of data.items) {
        const option = el("option", item.name + (item.isActive ? "" : " · Inactivo"));
        option.value = item.id; option.disabled = !item.isActive && item.id !== selected; select.append(option);
      }
      page++;
    } while (page <= totalPages);
    select.value = selected;
  }

  async function load() {
    save.disabled = true;
    current = await request(path);
    const product = { ...current.product, unitCost: current.unitCost, minimumStock: current.minimumStock };
    for (const name of ["Name", "Description", "UnitPrice", "UnitCost", "Material", "Species", "MinimumStock", "StockQuantity"])
      form.elements.namedItem(name).value = product[name[0].toLowerCase() + name.slice(1)];
    form.elements.namedItem("IsActive").checked = product.isActive;
    await Promise.all([lookup("categories", product.categoryId), lookup("suppliers", product.supplierId)]);
    sizes.replaceChildren(); current.sizes.forEach(sizeRow);
    form.elements.namedItem("Image").value = "";
    preview.classList.toggle("oculto", !current.imageUrl);
    if (current.imageUrl) preview.src = current.imageUrl;
    reloadButton.classList.add("oculto"); save.disabled = false;
  }

  const readFile = file => new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",")[1]);
    reader.onerror = () => reject(new Error("No se pudo leer la imagen seleccionada."));
    reader.readAsDataURL(file);
  });
  document.getElementById("product-edit-add-size").addEventListener("click", () => sizeRow());
  reloadButton.addEventListener("click", async () => {
    if (!confirm("¿Cargar la versión vigente y reemplazar los campos del formulario?")) return;
    try { await load(); clearIdentityErrors(form, feedback); }
    catch (error) { handleIdentityError(form, feedback, error); }
  });
  form.addEventListener("submit", async event => {
    event.preventDefault(); clearIdentityErrors(form, feedback);
    if (!form.reportValidity() || !current) return;
    save.disabled = true;
    try {
      const file = form.elements.namedItem("Image").files[0];
      if (file && file.size > 5 * 1024 * 1024) {
        const node = form.querySelector('[data-error="Image"]'); node.textContent = "Máximo 5 MB.";
        return;
      }
      const values = formValues(form);
      const body = {
        name: values.Name, description: values.Description, categoryId: values.CategoryId,
        supplierId: values.SupplierId, material: values.Material, species: values.Species,
        revision: current.revision, icon: current.product.icon,
        unitPrice: Number(values.UnitPrice), unitCost: Number(values.UnitCost),
        stockQuantity: Number(values.StockQuantity), minimumStock: Number(values.MinimumStock),
        isActive: form.elements.namedItem("IsActive").checked,
        sizes: [...sizes.children].map(row => Object.fromEntries([...row.querySelectorAll("input")].map(input =>
          [input.dataset.name, input.type === "number" ? Number(input.value) : input.value])))
      };
      if (file) { body.imageFileName = file.name; body.imageBase64Data = await readFile(file); }
      await request(path, "PUT", body);
      await load(); showIdentityMessage(feedback, "Producto actualizado correctamente.", "info");
    } catch (error) {
      handleIdentityError(form, feedback, error);
      if (error.status === 409) reloadButton.classList.remove("oculto");
    } finally { save.disabled = false; }
  });
  void load().catch(error => handleIdentityError(form, feedback, error));
});
