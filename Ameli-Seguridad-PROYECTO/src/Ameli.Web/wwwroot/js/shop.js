"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const { el, request, money, date } = window.ameliShop;
  const addForm = document.getElementById("product-add");
  if (addForm) {
    let command;
    addForm.addEventListener("submit", async event => {
      event.preventDefault(); const button = addForm.querySelector('button[type="submit"]'), feedback = document.getElementById("product-add-result");
      command ||= { requestId: crypto.randomUUID(), productId: addForm.dataset.id, size: addForm.elements.namedItem("Size").value, quantity: 1 };
      button.disabled = true;
      try { const result = await request("/account/shop/cart", "POST", command); feedback.textContent = result.message; feedback.append(document.createTextNode(" ")); const link = el("a", "Ir al carrito"); link.href = "/carrito"; feedback.append(link); button.textContent = "Agregado"; }
      catch (error) { feedback.textContent = error.message; button.disabled = false; if (error.status) command = null; }
    });
  }
  const cart = document.getElementById("shop-cart");
  if (cart) void initCart(cart);
  const orders = document.getElementById("shop-orders");
  if (orders) void initOrders(orders);
  const presentation = document.getElementById("product-presentation");
  if (presentation) void initPresentation(presentation);

  async function initCart(container) {
    const form = document.getElementById("shop-checkout"), select = document.getElementById("checkout-address"), feedback = document.getElementById("checkout-result"), button = document.getElementById("checkout-button");
    let command, hasItems = false;
    const reload = async () => {
      const data = await request("/account/shop/cart"); container.replaceChildren(); hasItems = data.lines.length > 0;
      if (!hasItems) container.append(el("p", "Tu carrito está vacío. Consulta el catálogo o pide recomendaciones en el chat.", "panel"));
      for (const line of data.lines) {
        const row = el("article", null, "panel"); row.append(el("h2", line.productName), el("p", `${line.sku} · Talla ${line.size} · ${line.quantity} unidades`), el("p", line.petName ? `Para: ${line.petName}` : "Sin mascota vinculada"),
          el("p", `${money(line.unitPrice)} por unidad · ${line.availableStock} disponibles ahora`));
        if (line.quantity > line.availableStock) row.append(el("p", "El stock cambió. Elimina esta selección y ajusta la cantidad antes de confirmar.", "feedback error"));
        const remove = el("button", "Eliminar del carrito", "boton secundario"); remove.type = "button";
        remove.addEventListener("click", async () => { remove.disabled = true; try { await request(`/account/shop/cart/${line.id}`, "DELETE"); command = null; await reload(); } catch (error) { feedback.textContent = error.message; remove.disabled = false; } }); row.append(remove); container.append(row);
      }
      container.append(el("strong", `Total: ${money(data.total)}`, "panel")); button.disabled = !hasItems;
    };
    try { await reload(); const addresses = await request("/account/profile/addresses"); for (const a of addresses) { const option = el("option", `${a.province}, ${a.canton}, ${a.district}: ${a.exactAddress}${a.isDefault ? " · Predeterminada" : ""}`); option.value = a.id; select.append(option); if (a.isDefault) select.value = a.id; } }
    catch (error) { feedback.textContent = error.message; button.disabled = true; }
    form.addEventListener("submit", async event => {
      event.preventDefault(); if (!select.value) { feedback.textContent = "Selecciona una dirección de entrega."; return; }
      if (!command && !confirm("¿Confirmas que quieres registrar este pedido?")) return;
      command ||= { requestId: crypto.randomUUID(), addressId: select.value }; button.disabled = true; select.disabled = true; feedback.textContent = "Registrando pedido…";
      try { const order = await request("/account/shop/checkout", "POST", command); location.assign("/mis-pedidos?numero=" + encodeURIComponent(order.number)); }
      catch (error) { feedback.textContent = error.message; button.disabled = false; button.textContent = "Reintentar confirmar"; if (error.status) { command = null; select.disabled = false; } }
    });
  }
  function orderCard(order) {
    const card = el("article", null, "panel order-card"); card.append(el("h2", order.number), el("strong", order.status),
      el("p", `Última actualización: ${date(order.updatedAtUtc)}`), el("p", `Transportista: ${order.carrier || "Sin asignar"}`), el("p", `Guía: ${order.trackingNumber || "Sin asignar"}`),
      el("p", `Dirección registrada: ${order.deliverySnapshot}`), el("p", `Total: ${money(order.total)}`));
    for (const item of order.items) card.append(el("p", `${item.quantity} × ${item.productName} · ${item.sku} · Talla ${item.size} · ${money(item.unitPrice)}`), el("p", `Perfil registrado: ${item.petSnapshot}`));
    return card;
  }
  async function initOrders(container) {
    const staff = container.dataset.staff === "true";
    try {
      const orders = await request(staff ? "/account/shop/staff/orders" : "/account/shop/orders"); container.replaceChildren();
      if (!orders.length) container.append(el("p", "No hay pedidos registrados.", "panel"));
      for (const order of orders) {
        const card = orderCard(order);
        if (staff) {
          const form = el("form", null, "formulario-apilado"), select = el("select"), statusLabel = el("label", "Estado ");
          for (const value of ["Confirmado", "En preparación", "Enviado", "Entregado", "Cancelado"]) { const option = el("option", value); option.value = value; select.append(option); } select.value = order.status; statusLabel.append(select);
          const carrier = el("input"); carrier.value = order.carrier; carrier.maxLength = 100; const carrierLabel = el("label", "Transportista "); carrierLabel.append(carrier);
          const tracking = el("input"); tracking.value = order.trackingNumber; tracking.maxLength = 100; const trackingLabel = el("label", "Guía "); trackingLabel.append(tracking);
          const save = el("button", "Guardar estado", "boton primario"); save.type = "submit"; const result = el("p"); result.setAttribute("role", "status");
          form.append(statusLabel, carrierLabel, trackingLabel, save, result); card.append(form);
          form.addEventListener("submit", async event => { event.preventDefault(); save.disabled = true; try { await request(`/account/shop/staff/orders/${order.id}`, "PUT", { status: select.value, carrier: carrier.value, trackingNumber: tracking.value }); await initOrders(container); } catch (error) { result.textContent = error.message; save.disabled = false; } });
        }
        container.append(card);
      }
    } catch (error) { container.replaceChildren(el("p", error.message, "feedback error")); }
  }
  async function initPresentation(section) {
    const form = document.getElementById("presentation-form"), errorBox = document.getElementById("presentation-error"), rows = document.getElementById("presentation-sizes");
    const path = `/account/shop/admin/products/${section.dataset.id}`;
    const row = size => {
      const node = el("div", null, "presentation-size-row");
      for (const [name, label] of [["Name", "Talla"], ["MinHeightCm", "Altura mín. cm"], ["MaxHeightCm", "Altura máx. cm"], ["MinWeightKg", "Peso mín. kg"], ["MaxWeightKg", "Peso máx. kg"], ["Equivalent", "Equivalencia"]]) {
        const input = el("input"), wrapper = el("label", label); input.dataset.name = name; input.value = size?.[name[0].toLowerCase() + name.slice(1)] ?? "";
        if (name.includes("Cm") || name.includes("Kg")) { input.type = "number"; input.min = "0.01"; input.step = "0.01"; } else input.maxLength = name === "Name" ? 20 : 80;
        wrapper.append(input); node.append(wrapper);
      }
      const remove = el("button", "Quitar", "boton secundario"); remove.type = "button"; remove.addEventListener("click", () => node.remove()); node.append(remove); rows.append(node);
    };
    const load = async () => {
      const product = await request(path); document.getElementById("presentation-title").textContent = product.name;
      for (const name of ["Sku", "Material", "UnitPrice", "StockQuantity", "GarmentType", "Style", "CareInstructions", "FitNotes"]) form.elements.namedItem(name).value = product[name[0].toLowerCase() + name.slice(1)];
      form.elements.namedItem("Colors").value = product.colors.join(", "); form.elements.namedItem("IsActive").checked = product.isActive;
      rows.replaceChildren(); product.sizes.forEach(row);
    };
    document.getElementById("presentation-add-size").addEventListener("click", () => row());
    try { await load(); } catch (error) { errorBox.textContent = error.message; errorBox.classList.remove("oculto"); form.querySelector('button[type="submit"]').disabled = true; }
    form.addEventListener("submit", async event => {
      event.preventDefault(); clearIdentityErrors(form, errorBox); const values = formValues(form);
      values.UnitPrice = Number(values.UnitPrice); values.StockQuantity = Number(values.StockQuantity); values.IsActive = form.elements.namedItem("IsActive").checked;
      values.Colors = values.Colors.split(/[,;]/).map(c => c.trim()).filter(Boolean);
      values.Sizes = [...rows.children].map(node => Object.fromEntries([...node.querySelectorAll("input")].map(input => [input.dataset.name, input.type === "number" ? Number(input.value) : input.value])));
      const save = form.querySelector('button[type="submit"]'); save.disabled = true;
      try { await request(path, "PUT", values); await load(); showIdentityMessage(errorBox, "Ficha guardada. El chat consultará estos datos actualizados.", "info"); }
      catch (error) { handleIdentityError(form, errorBox, error); }
      finally { save.disabled = false; }
    });
  }
});
