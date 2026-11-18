"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const { el, request, money, date } = window.ameliShop;
  document.querySelectorAll(".cart-add-form").forEach(window.ameliShop.bindCartForm);
  const cart = document.getElementById("shop-cart");
  if (cart) void initCart(cart);
  const orders = document.getElementById("shop-orders");
  if (orders) void initOrders(orders);
  const presentation = document.getElementById("product-presentation");
  if (presentation) void initPresentation(presentation);

  async function initCart(container) {
    const form = document.getElementById("shop-checkout");
    const select = document.getElementById("checkout-address");
    const card = document.getElementById("checkout-card");
    const feedback = document.getElementById("checkout-result");
    const button = document.getElementById("checkout-button");
    let command = null, current = null, pending = false;

    const reload = async () => {
      current = await request("/account/shop/cart");
      container.replaceChildren();
      form.classList.toggle("oculto", current.lines.length === 0);
      if (!current.lines.length) {
        const empty = el("div", null, "panel");
        const link = el("a", "Ir al catálogo", "boton primario"); link.href = "/productos";
        empty.append(el("p", "Tu carrito está vacío."), link); container.append(empty);
      }
      for (const line of current.lines) {
        const row = el("article", null, "panel");
        row.dataset.lineId = line.id;
        row.append(el("h2", line.productName), el("p", line.size ? `Talla ${line.size}` : "Sin talla especificada"),
          el("p", line.petName ? `Para: ${line.petName}` : "Sin mascota vinculada"),
          el("p", `${money(line.unitPrice)} por unidad · Subtotal ${money(line.subtotal)}`));
        if (line.issue) row.append(el("p", line.issue, "feedback error"));
        const input = el("input"); input.type = "number"; input.min = "1"; input.step = "1";
        input.max = String(line.maximumQuantity); input.value = String(Math.min(line.quantity, line.maximumQuantity));
        input.required = true; input.setAttribute("aria-label", `Cantidad de ${line.productName}`);
        const label = el("label", `Cantidad (máximo ${line.maximumQuantity}) `); label.append(input);
        const save = el("button", line.issue ? "Confirmar cantidad disponible" : "Guardar cantidad", "boton primario");
        save.type = "button"; save.disabled = line.maximumQuantity === 0;
        const result = el("p"); result.setAttribute("role", "status");
        save.addEventListener("click", async () => {
          if (!input.reportValidity()) return;
          const quantity = Number(input.value);
          if (!Number.isInteger(quantity) || quantity < 1) { result.textContent = "Indica una cantidad entera mayor que cero."; return; }
          save.disabled = true; button.disabled = true;
          try {
            await request(`/account/shop/cart/${line.id}`, "PUT", { quantity, revision: line.revision });
            command = null; await reload();
          } catch (error) { result.textContent = error.message; save.disabled = false; button.disabled = !current.canCheckout; }
        });
        const remove = el("button", "Eliminar", "boton secundario"); remove.type = "button";
        remove.addEventListener("click", async () => {
          if (!confirm(`¿Eliminar ${line.productName} del carrito?`)) return;
          remove.disabled = true; button.disabled = true;
          try { await request(`/account/shop/cart/${line.id}`, "DELETE"); command = null; await reload(); }
          catch (error) { result.textContent = error.message; remove.disabled = false; button.disabled = !current.canCheckout; }
        });
        row.append(label, save, remove, result); container.append(row);
      }
      const totals = el("section", null, "panel cart-totals");
      totals.append(el("p", `Subtotal: ${money(current.subtotal)}`), el("p", `Descuentos: ${money(current.discount)}`),
        el("p", `Impuestos: ${money(current.tax)}`), el("strong", `Total: ${money(current.total)}`));
      container.append(totals); button.disabled = !current.canCheckout;
    };
    try {
      await reload();
      const addresses = await request("/account/profile/addresses");
      for (const address of addresses) {
        const option = el("option", `${address.province}, ${address.canton}, ${address.district}: ${address.exactAddress}`);
        option.value = address.id; select.append(option); if (address.isDefault) select.value = address.id;
      }
    } catch (error) { feedback.textContent = error.message; button.disabled = true; }

    window.addEventListener("focus", () => { if (!pending && !command) void reload().catch(error => { feedback.textContent = error.message; button.disabled = true; }); });
    form.addEventListener("submit", async event => {
      event.preventDefault();
      if (pending) return;
      for (const node of form.querySelectorAll("[data-checkout-error]")) node.textContent = "";
      if (!select.value) { feedback.textContent = "Dirección de envío no especificada"; return; }
      if (!current?.canCheckout || !form.reportValidity()) return;
      if (!command && !confirm("¿Confirmar el pedido con estos productos, importes y dirección?")) return;
      command ||= {
        requestId: crypto.randomUUID(), addressId: select.value, cartRevision: current.revision,
        cardNumber: form.elements.namedItem("CardNumber").value,
        expirationDate: form.elements.namedItem("ExpirationDate").value,
        cardholderName: form.elements.namedItem("CardholderName").value,
        cvc: form.elements.namedItem("Cvc").value
      };
      pending = true; button.disabled = true; select.disabled = true; card.disabled = true; feedback.textContent = "Confirmando pedido…";
      try {
        const order = await request("/account/shop/checkout", "POST", command);
        form.elements.namedItem("Cvc").value = "";
        location.assign("/mis-pedidos?numero=" + encodeURIComponent(order.number));
      } catch (error) {
        feedback.textContent = error.message; button.textContent = "Reintentar confirmar";
        for (const [name, messages] of Object.entries(error.errors || {})) {
          const target = form.querySelector(`[data-checkout-error="${name}"]`);
          if (target) target.textContent = messages.join(" ");
        }
        if (error.status >= 400 && error.status < 500) {
          command = null; select.disabled = false; card.disabled = false;
          if (error.code === "payment_declined") form.elements.namedItem("Cvc").value = "";
          if (error.status === 409) await reload();
        }
        button.disabled = !current?.canCheckout;
      } finally { pending = false; }
    });
  }

  function orderCard(order) {
    const card = el("article", null, "panel order-card");
    if (new URLSearchParams(location.search).get("numero") === order.number) card.append(el("p", "Pedido confirmado", "feedback info"));
    card.append(el("h2", order.number), el("strong", order.status),
      el("p", `Última actualización: ${date(order.updatedAtUtc)}`),
      el("p", `Transportista: ${order.carrier || "Sin asignar"}`), el("p", `Guía: ${order.trackingNumber || "Sin asignar"}`),
      el("p", `Dirección registrada: ${order.deliverySnapshot}`),
      el("p", `Subtotal: ${money(order.subtotal)} · Descuentos: ${money(order.discount)} · Impuestos: ${money(order.tax)}`),
      el("p", `Total: ${money(order.total)}`));
    if (order.paymentMode === "Demo") card.append(el("p", "Pago de prueba aprobado."));
    const history = el("ol", null, "order-history");
    for (const step of order.history || []) {
      history.append(el("li", `${step.status} · ${date(step.occurredAtUtc)}${step.carrier ? " · " + step.carrier : ""}${step.trackingNumber ? " · Guía " + step.trackingNumber : ""}`));
    }
    card.append(history);
    for (const item of order.items) card.append(el("p", `${item.quantity} × ${item.productName} · ${item.sku} · ${item.size || "Sin talla especificada"} · ${money(item.unitPrice)}`), el("p", `Perfil registrado: ${item.petSnapshot}`));
    return card;
  }

  async function initOrders(container) {
    const staff = container.dataset.staff === "true";
    try {
      const orders = await request(staff ? "/account/shop/staff/orders" : "/account/shop/orders");
      container.replaceChildren();
      if (!orders.length) container.append(el("p", "No hay pedidos anteriores o en camino", "panel"));
      for (const order of orders) {
        const card = orderCard(order);
        if (staff) {
          const form = el("form", null, "formulario-apilado"), select = el("select"), label = el("label", "Estado ");
          for (const value of ["Confirmado", "En preparación", "En camino", "Entregado", "Cancelado"]) { const option = el("option", value); option.value = value; select.append(option); }
          select.value = order.status; label.append(select);
          const carrier = el("input"); carrier.value = order.carrier; carrier.maxLength = 100;
          const carrierLabel = el("label", "Transportista "); carrierLabel.append(carrier);
          const tracking = el("input"); tracking.value = order.trackingNumber; tracking.maxLength = 100;
          const trackingLabel = el("label", "Guía "); trackingLabel.append(tracking);
          const save = el("button", "Guardar estado", "boton primario"); save.type = "submit";
          const result = el("p"); result.setAttribute("role", "status");
          form.append(label, carrierLabel, trackingLabel, save, result); card.append(form);
          form.addEventListener("submit", async event => {
            event.preventDefault(); save.disabled = true;
            try { await request(`/account/shop/staff/orders/${order.id}`, "PUT", { status: select.value, carrier: carrier.value, trackingNumber: tracking.value }); await initOrders(container); }
            catch (error) { result.textContent = error.message; save.disabled = false; }
          });
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
