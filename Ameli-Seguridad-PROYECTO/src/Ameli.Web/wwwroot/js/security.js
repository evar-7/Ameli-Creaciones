"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const lockout = document.getElementById("lockout-remaining");
  if (lockout) {
    const until = Number(lockout.dataset.lockoutUntil);
    const update = () => {
      const seconds = Math.max(0, Math.ceil((until - Date.now()) / 1000));
      if (seconds) lockout.textContent = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
      else {
        document.getElementById("lockout-message").textContent = "El bloqueo terminó. Ya puedes intentar ingresar.";
        clearInterval(timer);
      }
    };
    const timer = setInterval(update, 1000);
    update();
  }
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
      const rules = {
        length: value.length >= 8 && value.length <= 64,
        upper: /\p{Lu}/u.test(value), lower: /\p{Ll}/u.test(value), digit: /\p{Nd}/u.test(value),
        symbol: /[^\p{L}\p{N}\s]/u.test(value)
      };
      const messages = {
        length: "La contraseña debe tener entre 8 y 64 caracteres.", upper: "Agrega al menos una letra mayúscula.",
        lower: "Agrega al menos una letra minúscula.", digit: "Agrega al menos un número.",
        symbol: "Agrega al menos un carácter especial (símbolo)."
      };
      form.querySelectorAll("[data-password-rule]").forEach(item => item.classList.toggle("is-met", rules[item.dataset.passwordRule]));
      password.setCustomValidity(Object.keys(rules).filter(key => !rules[key]).map(key => messages[key]).join(" "));
      confirmation.setCustomValidity(value === confirmation.value ? "" : "Las contraseñas deben coincidir.");
    };
    password.addEventListener("input", validate);
    confirmation.addEventListener("input", validate);
  });
  document.querySelectorAll("form").forEach(form => form.addEventListener("submit", () => {
    if (form.matches("[data-password-reset]")) return;
    if (!form.checkValidity()) return;
    form.querySelectorAll("button[data-pending]").forEach(button => { button.disabled = true; button.textContent = button.dataset.pending; });
  }));
  const resetForm = document.querySelector("form[data-password-reset]");
  if (resetForm) {
    const feedback = document.getElementById("reset-feedback");
    const submit = resetForm.querySelector('button[type="submit"]');
    const originalLabel = submit.textContent;
    resetForm.addEventListener("submit", async event => {
      event.preventDefault();
      if (submit.disabled || !resetForm.reportValidity()) return;
      feedback.classList.add("oculto");
      submit.disabled = true; submit.textContent = submit.dataset.pending;
      try {
        const response = await fetch(resetForm.action, { method: "POST", body: new FormData(resetForm),
          headers: { Accept: "application/json" }, credentials: "same-origin", cache: "no-store" });
        if (response.ok) { window.location.assign("/ingresar?estado=restablecida"); return; }
        let error = {};
        try { error = await response.json(); } catch { /* Show a generic error below. */ }
        const unmet = Object.values(error.errors || {}).flat();
        feedback.textContent = unmet.length ? unmet.join(" ") : (error.message || "No se pudo guardar. Intenta de nuevo.");
        feedback.classList.remove("oculto"); feedback.focus();
        if (error.code === "invalid_reset") {
          document.getElementById("retry-recovery").classList.remove("oculto");
          submit.disabled = true;
        }
      } catch {
        feedback.textContent = "No se pudo conectar. Comprueba la conexión e intenta de nuevo.";
        feedback.classList.remove("oculto"); feedback.focus();
      } finally {
        if (document.getElementById("retry-recovery").classList.contains("oculto")) submit.disabled = false;
        submit.textContent = originalLabel;
      }
    });
  }
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
