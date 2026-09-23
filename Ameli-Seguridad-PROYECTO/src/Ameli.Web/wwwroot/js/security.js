"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const menuButton = document.querySelector(".boton-menu");
  const menu = document.getElementById("menuLateral");
  menuButton?.addEventListener("click", () => menuButton.setAttribute("aria-expanded", String(menu.classList.toggle("abierto"))));
  document.querySelectorAll("[data-ver-clave]").forEach(button => button.addEventListener("click", () => {
    const input = document.getElementById(button.getAttribute("aria-controls"));
    const show = input.type === "password";
    input.type = show ? "text" : "password";
    button.textContent = show ? "Ocultar" : "Ver";
    button.setAttribute("aria-label", show ? "Ocultar contraseña" : "Mostrar contraseña");
  }));
  document.querySelectorAll("[data-open-dialog]").forEach(button => button.addEventListener("click", () => document.getElementById(button.dataset.openDialog)?.showModal()));
  document.querySelectorAll("[data-close-dialog]").forEach(button => button.addEventListener("click", () => button.closest("dialog").close()));
  document.querySelectorAll("form[data-password-confirm]").forEach(form => {
    const password = form.elements.namedItem("password");
    const confirmation = form.elements.namedItem("confirmPassword");
    const validate = () => {
      const value = password.value;
      const strong = value.length >= 8 && value.length <= 64 && /\p{Lu}/u.test(value) && /\p{Ll}/u.test(value) && /\p{Nd}/u.test(value) && /[^\p{L}\p{N}\s]/u.test(value);
      password.setCustomValidity(strong ? "" : "Usa de 8 a 64 caracteres, mayúscula, minúscula, número y símbolo.");
      confirmation.setCustomValidity(value === confirmation.value ? "" : "Las contraseñas deben coincidir.");
    };
    password.addEventListener("input", validate);
    confirmation.addEventListener("input", validate);
  });
  document.querySelectorAll("form").forEach(form => form.addEventListener("submit", () => {
    if (!form.checkValidity()) return;
    form.querySelectorAll("button[data-pending]").forEach(button => { button.disabled = true; button.textContent = button.dataset.pending; });
  }));
  window.addEventListener("pageshow", event => { if (event.persisted) window.location.reload(); });
  const control = document.getElementById("session-control");
  if (control) startSession(control);
});

function startSession(control) {
  const csrf = control.querySelector('input[name="__RequestVerificationToken"]').value;
  const warning = document.getElementById("session-warning");
  let expires = Math.min(Date.parse(control.dataset.idleExpires), Date.parse(control.dataset.absoluteExpires));
  let lastSent = 0, pending, sending = false;
  const channel = "BroadcastChannel" in window ? new BroadcastChannel("ameli-session") : null;
  const end = () => { window.location.replace("/ingresar?estado=expirada"); };
  const update = status => {
    expires = Math.min(Date.parse(status.session.idleExpiresAtUtc), Date.parse(status.session.absoluteExpiresAtUtc));
    warning.classList.toggle("oculto", expires - Date.now() > 60000);
    channel?.postMessage({ expires });
  };
  if (channel) channel.onmessage = event => { if (Number.isFinite(event.data?.expires)) expires = event.data.expires; };
  const request = async active => {
    if (sending) return;
    if (Date.now() >= expires) { end(); return; }
    sending = true;
    try {
      const response = await fetch(active ? "/account/activity" : "/account/session", {
        method: active ? "POST" : "GET", credentials: "same-origin", cache: "no-store", keepalive: active,
        headers: active ? { "X-CSRF-TOKEN": csrf } : {}
      });
      if (response.status === 401) { end(); return; }
      if (response.ok) { update(await response.json()); if (active) lastSent = Date.now(); }
    } catch { /* Offline errors never extend the deadline. The API remains authoritative. */ }
    finally { sending = false; }
  };
  const activity = event => {
    if (!event.isTrusted) return;
    clearTimeout(pending);
    if (Date.now() - lastSent >= 30000) void request(true);
    else pending = setTimeout(() => void request(true), 1000);
  };
  ["pointerdown", "keydown", "input", "wheel"].forEach(name => document.addEventListener(name, activity, { passive: true }));
  document.getElementById("keep-session")?.addEventListener("click", () => void request(true));
  // Passive checks detect revocation across devices and do not count as user activity.
  setInterval(() => { if (document.visibilityState === "visible") void request(false); }, 30000);
  setInterval(() => {
    if (Date.now() >= expires) { end(); return; }
    warning.classList.toggle("oculto", expires - Date.now() > 60000);
  }, 1000);
  document.addEventListener("visibilitychange", () => { if (document.visibilityState === "visible") void request(false); });
}
