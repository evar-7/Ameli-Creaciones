"use strict";
window.ameliFulfillment = (() => {
  const { el, request, date } = window.ameliShop;
  function actionForm(card, action, title, buttonText) {
    const form = el("form", null, "formulario-apilado order-action"); form.dataset.action = action; form.noValidate = true;
    const result = el("div", null, "feedback oculto"); result.setAttribute("role", "alert"); result.tabIndex = -1;
    const button = el("button", buttonText, "boton primario"); button.type = "submit";
    const itemsError = el("small", "", "field-error"); itemsError.dataset.error = "Items";
    form.append(el("h3", title), result, itemsError); card.append(form);
    return { form, result, button, finish() { form.append(button); } };
  }
  function field(form, name, title, { type = "text", value = "", required = true, maxLength = 200 } = {}) {
    const label = el("label", title), input = el(type === "textarea" ? "textarea" : "input");
    if (type !== "textarea") input.type = type;
    input.name = name; input.value = value; input.required = required; input.maxLength = maxLength;
    const error = el("small", "", "field-error"); error.dataset.error = name;
    label.append(input, error); form.append(label); return input;
  }
  function quantities(form, items, initial) {
    const group = el("fieldset"); group.append(el("legend", "Artículos")); form.append(group);
    const inputs = new Map();
    for (const item of items) {
      const input = field(group, item.id, `${item.productName} · ${item.size || "Sin talla"} · Pedido: ${item.quantity} · Reservado: ${item.reservedQuantity}`, { type: "number", value: initial(item) });
      input.min = "0"; input.max = String(item.quantity); input.step = "1"; inputs.set(item.id, input);
    }
    const error = el("small", "", "field-error"); error.dataset.error = "Items"; form.append(error);
    return () => items.map(item => ({ itemId: item.id, quantity: Number(inputs.get(item.id).value) }));
  }
  function bind(action, send, refresh, confirmation) {
    let pending = false;
    action.form.addEventListener("submit", async event => {
      event.preventDefault(); if (pending) return;
      clearIdentityErrors(action.form, action.result);
      if (!action.form.reportValidity() || confirmation && !confirm(confirmation)) return;
      pending = true; action.button.disabled = true;
      try { await send(); await refresh(); }
      catch (error) { handleIdentityError(action.form, action.result, error); }
      finally { pending = false; action.button.disabled = false; }
    });
    action.finish();
  }
  function deliverySummary(order, card) {
    const f = order.fulfillment;
    if (!f?.deliveredAtUtc) return;
    card.append(el("p", `Entrega: ${date(f.deliveredAtUtc)} · Registrada por ${f.deliveryUserName || "Sin registro anterior"} el ${date(f.deliveryRecordedAtUtc || f.deliveredAtUtc)}`),
      el("p", `Referencia de recepción: ${f.receiptReference || "Sin referencia anterior"}`));
  }
  function renderStaff(order, card, context, refresh) {
    const f = order.fulfillment || {}, path = `/account/shop/staff/orders/${order.id}`;
    card.append(el("p", `Pago: ${f.paymentStatus || "Pendiente de verificación"}`));
    if (f.responsibleName) card.append(el("p", `Responsable: ${f.responsibleName} · Inicio: ${date(f.preparationStartedAtUtc)}`));
    if (f.inventoryAlert) card.append(el("p", "Alerta de inventario: " + f.inventoryAlert, "feedback error"));
    for (const incident of order.incidents || []) card.append(el("p", `Incidencia: ${incident.detail} · ${incident.reportedByName} · ${date(incident.occurredAtUtc)}`));
    if (order.status === "Confirmado" || order.status === "En preparación" && !f.responsibleUserId) {
      const start = actionForm(card, "prepare", "Preparación", "Iniciar preparación");
      bind(start, () => request(path + "/prepare", "POST", {}), refresh);
      if (context.admin === "true" && (f.inventoryAlert || order.items.some(item => item.quantity !== item.reservedQuantity))) {
        const correction = actionForm(card, "reservations", "Corregir reserva", "Confirmar corrección de reserva");
        const reason = field(correction.form, "Reason", "Motivo de la corrección", { type: "textarea", maxLength: 500 }); reason.minLength = 5;
        correction.form.append(el("p", "La corrección reserva las unidades pendientes desde las existencias disponibles."));
        bind(correction, () => request(path + "/reservations", "PUT", { reason: reason.value, items: order.items.map(item => ({ itemId: item.id, quantity: item.quantity })) }), refresh, "¿Aplicar la corrección de reserva y sus movimientos de inventario?");
      }
    }
    if (order.status === "En preparación" && f.responsibleUserId && (context.admin === "true" || context.userId === f.responsibleUserId)) {
      const pack = actionForm(card, "packing", "Verificación y empaque", "Guardar verificación de empaque");
      const packed = quantities(pack.form, order.items, item => item.packedQuantity);
      bind(pack, () => request(path + "/packing", "PUT", { items: packed() }), refresh);
      const dispatch = actionForm(card, "dispatch", "Despacho", "Marcar como En camino");
      const carrier = field(dispatch.form, "Carrier", "Transportista", { maxLength: 100 });
      const tracking = field(dispatch.form, "TrackingNumber", "Número de guía único", { maxLength: 100 });
      const error = el("small", "", "field-error"); error.dataset.error = "Items"; dispatch.form.append(error);
      if (order.items.some(item => item.packedQuantity !== item.quantity)) dispatch.form.append(el("p", "Primero guarda la verificación de todos los artículos empacados."));
      bind(dispatch, () => request(path + "/dispatch", "POST", { carrier: carrier.value, trackingNumber: tracking.value }), refresh, "¿Confirmar el despacho y notificar al cliente?");
    }
    if (order.status === "En camino") {
      const delivery = actionForm(card, "deliver", "Confirmación de entrega completa", "Marcar como Entregado");
      const received = quantities(delivery.form, order.items, () => 0);
      const at = field(delivery.form, "DeliveredAtUtc", "Fecha y hora de entrega", { type: "datetime-local" });
      const proof = field(delivery.form, "CarrierConfirmation", "Confirmación del transportista (referencia o comprobante)");
      const receipt = field(delivery.form, "ReceiptReference", "Referencia de recepción");
      bind(delivery, () => request(path + "/deliver", "POST", { items: received(), deliveredAtUtc: new Date(at.value).toISOString(), carrierConfirmation: proof.value, receiptReference: receipt.value }), refresh, "¿Confirmar la entrega de todos los artículos?");
      const incident = actionForm(card, "incidents", "Artículos faltantes", "Registrar incidencia");
      const detail = field(incident.form, "Detail", "Describe la incidencia", { type: "textarea", maxLength: 1000 }); detail.minLength = 5;
      incident.form.append(el("p", "Se registrarán como faltantes las diferencias de las cantidades recibidas indicadas arriba."));
      const error = el("small", "", "field-error"); error.dataset.error = "Items"; incident.form.append(error);
      let command;
      bind(incident, async () => {
        const values = received();
        command ||= { requestId: crypto.randomUUID(), detail: detail.value, missingItems: order.items.map(item => ({ itemId: item.id, quantity: item.quantity - values.find(value => value.itemId === item.id).quantity })).filter(item => item.quantity > 0) };
        try { return await request(path + "/incidents", "POST", command); }
        catch (error) { if (error.status >= 400 && error.status < 500) command = null; throw error; }
      }, refresh);
    }
    if (order.status === "Confirmado" || order.status === "En preparación") {
      const cancel = actionForm(card, "cancel", "Cancelar antes del despacho", "Cancelar pedido");
      bind(cancel, () => request(path, "PUT", { status: "Cancelado" }), refresh, "¿Cancelar el pedido y devolver sus unidades reservadas al inventario?");
    }
    deliverySummary(order, card);
    if (order.status === "Entregado") card.append(el("p", "Seguimiento cerrado. Una nueva confirmación conserva la entrega original."));
  }
  function renderCustomer(order, card, refresh) {
    deliverySummary(order, card);
    if (order.fulfillment?.pointsEnabled) card.append(el("p", "Compra habilitada para el programa de puntos."));
    if (order.rating) { card.append(el("p", `Tu calificación: ${order.rating.stars}/5 · ${order.rating.comment}`)); return; }
    if (!order.fulfillment?.ratingEnabled) return;
    const rating = actionForm(card, "rating", "Califica tu compra", "Guardar calificación");
    const stars = field(rating.form, "Stars", "Estrellas (1 a 5)", { type: "number", value: "5" }); stars.min = "1"; stars.max = "5"; stars.step = "1";
    const comment = field(rating.form, "Comment", "Comentario", { type: "textarea", required: false, maxLength: 1000 });
    bind(rating, () => request(`/account/shop/orders/${order.id}/rating`, "POST", { stars: Number(stars.value), comment: comment.value }), refresh);
  }
  return { renderStaff, renderCustomer };
})();
