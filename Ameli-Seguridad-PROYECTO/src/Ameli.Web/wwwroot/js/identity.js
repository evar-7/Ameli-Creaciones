"use strict";
document.addEventListener("DOMContentLoaded", () => {
  const registration = document.getElementById("customer-registration");
  if (registration) initRegistration(registration);
  const pets = document.getElementById("pets-list");
  if (pets) initPets(pets);
  const editor = document.getElementById("pet-editor");
  if (editor) initPetEditor(editor);
  const preferences = document.getElementById("preferences-editor");
  if (preferences) initPreferences(preferences);
  const addresses = document.getElementById("address-list");
  if (addresses) initAddresses(addresses);
});

function identityToken() {
  return document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
}

async function identityRequest(path, method = "GET", body) {
  const headers = { Accept: "application/json" };
  if (method !== "GET") headers["X-CSRF-TOKEN"] = identityToken();
  if (body !== undefined && !(body instanceof FormData)) headers["Content-Type"] = "application/json";
  const response = await fetch(path, {
    method, headers, credentials: "same-origin", cache: "no-store",
    body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body)
  });
  if (response.status === 401) { location.assign("/ingresar?estado=expirada"); throw new Error("Sesión expirada."); }
  if (response.status === 204) return null;
  let result;
  try { result = await response.json(); } catch { throw new Error("No se pudo leer la respuesta."); }
  if (!response.ok) throw result;
  return result;
}

function showIdentityMessage(node, text, kind = "error") {
  node.textContent = text;
  node.classList.remove("oculto", "error", "info");
  node.classList.add(kind);
  node.focus();
}

function clearIdentityErrors(form, message) {
  message.classList.add("oculto");
  form.querySelectorAll("[aria-invalid]").forEach(element => element.removeAttribute("aria-invalid"));
  form.querySelectorAll("[data-error]").forEach(element => { element.textContent = ""; });
}

function handleIdentityError(form, message, error) {
  const missing = error?.code === "required_fields";
  showIdentityMessage(message, error?.message || (missing ? "Campos obligatorios incompletos" : "No se pudo guardar. Intenta nuevamente."));
  for (const [name, texts] of Object.entries(error?.errors || {})) {
    const field = form.elements.namedItem(name);
    if (field?.setAttribute) field.setAttribute("aria-invalid", "true");
    const target = [...form.querySelectorAll("[data-error]")].find(node => node.dataset.error.toLowerCase() === name.toLowerCase());
    if (target) target.textContent = texts.join(" ");
  }
}

function formValues(form) {
  const values = Object.fromEntries(new FormData(form).entries());
  delete values.__RequestVerificationToken;
  return values;
}

function initRegistration(form) {
  const message = document.getElementById("registration-error");
  form.addEventListener("submit", async event => {
    event.preventDefault(); clearIdentityErrors(form, message);
    const button = form.querySelector('button[type="submit"]'); button.disabled = true;
    try {
      await identityRequest("/account/registration/create", "POST", formValues(form));
      location.assign("/ingresar?estado=registrada");
    } catch (error) { handleIdentityError(form, message, error); }
    finally { button.disabled = false; }
  });
}

function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  if (className) node.className = className;
  return node;
}

function petCard(pet) {
  const card = element("article", undefined, "panel pet-card");
  const picture = element("div", undefined, "pet-picture");
  if (pet.hasPhoto) {
    const img = document.createElement("img");
    img.src = `/account/profile/pets/${pet.id}/photo`;
    img.alt = `Foto de ${pet.name}`; picture.append(img);
  } else picture.append(element("span", "🐾"));
  const details = element("div", undefined, "pet-details");
  details.append(element("h2", pet.name), element("p", `${pet.species} · ${pet.breed}`),
    element("p", `Edad: ${pet.ageYears} años · Peso: ${pet.weightKg} kg · Talla: ${pet.heightCm} cm`),
    element("p", `Alergias: ${pet.allergies || (pet.allergiesDeclared ? "Sin alergias conocidas" : "No declaradas")}`),
    element("p", `Registrada: ${new Date(pet.createdAtUtc).toLocaleDateString("es-CR")}`),
    element("p", `Actualizado: ${new Date(pet.updatedAtUtc).toLocaleDateString("es-CR")}`),
    element("small", pet.isActive ? "Activo" : "Inactivo", pet.isActive ? "state-active" : "state-inactive"));
  const links = element("div", undefined, "grupo-botones");
  const edit = element("a", "Editar perfil", "boton secundario"); edit.href = `/perfil/mascotas/${pet.id}`;
  const pref = element("a", "Preferencias de estilo", "boton claro"); pref.href = `/perfil/mascotas/${pet.id}/preferencias`;
  links.append(edit, pref); details.append(links); card.append(picture, details); return card;
}

async function initPets(container) {
  try {
    const pets = await identityRequest("/account/profile/pets");
    container.replaceChildren();
    if (!pets.length) {
      const link = element("a", "Registrar mascota", "boton primario"); link.href = "/perfil/mascotas/nueva";
      const empty = element("div", undefined, "panel identity-empty"); empty.append(link); container.append(empty);
      return;
    }
    pets.forEach(pet => container.append(petCard(pet)));
    const link = element("a", "Registrar mascota", "boton primario"); link.href = "/perfil/mascotas/nueva";
    const actions = element("div", undefined, "pets-add"); actions.append(link); container.append(actions);
  } catch { container.textContent = "No se pudieron cargar tus mascotas. Recarga la página."; }
}

async function initPetEditor(section) {
  const id = section.dataset.id;
  const form = document.getElementById("pet-form");
  const message = document.getElementById("pet-error");
  let pet;
  const load = async () => {
    pet = await identityRequest(`/account/profile/pets/${id}`);
    for (const [name, value] of Object.entries(pet)) {
      if (name === "isActive" || name === "allergiesDeclared") continue;
      const field = form.elements.namedItem(name[0].toUpperCase() + name.slice(1));
      if (field && value != null) field.value = String(value);
    }
    form.elements.namedItem("IsActive").checked = pet.isActive;
    form.elements.namedItem("AllergiesDeclared").checked = pet.allergiesDeclared;
    const state = document.getElementById("pet-state");
    state.textContent = pet.isActive ? "Desactivar perfil" : "Reactivar perfil";
    document.getElementById("pet-state-label").textContent = pet.isActive ? "Perfil activo para futuras recomendaciones." : "Perfil inactivo; sus datos siguen visibles.";
  };
  if (id) {
    try { await load(); } catch { showIdentityMessage(message, "No se pudo cargar esta mascota."); }
    document.getElementById("pet-state").addEventListener("click", async () => {
      const action = pet.isActive ? "desactivar" : "reactivar";
      if (!confirm(`¿Quieres ${action} este perfil?`)) return;
      try {
        const result = await identityRequest(`/account/profile/pets/${id}/${pet.isActive ? "deactivate" : "reactivate"}`, "POST");
        await load(); showIdentityMessage(message, result.message, "info");
      } catch (error) { handleIdentityError(form, message, error); }
    });
  }
  form.addEventListener("submit", async event => {
    event.preventDefault(); clearIdentityErrors(form, message);
    const button = form.querySelector('button[type="submit"]'); button.disabled = true;
    try {
      const values = formValues(form);
      values.AllergiesDeclared = form.elements.namedItem("AllergiesDeclared").checked;
      delete values.Photo;
      if (!id) delete values.Revision;
      else {
        values.IsActive = form.elements.namedItem("IsActive").checked;
        const photoInput = form.elements.namedItem("Photo");
        const file = photoInput.files[0];
        if (file) {
          if (file.size > 5 * 1024 * 1024) {
            handleIdentityError(form, message, { message: "No se admiten imagenes superiores a 5 MB", errors: { Photo: ["No se admiten imagenes superiores a 5 MB"] } });
            return;
          }
          const data = await new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = () => reject(new Error("No se pudo leer la imagen."));
            reader.readAsDataURL(file);
          });
          values.PhotoFileName = file.name;
          values.PhotoBase64Data = data.substring(data.indexOf(",") + 1);
        }
      }
      const result = await identityRequest(id ? `/account/profile/pets/${id}` : "/account/profile/pets",
        id ? "PUT" : "POST", values);
      if (!id) { location.assign(`/perfil/mascotas/${result.id}`); return; }
      form.elements.namedItem("Photo").value = "";
      await load(); showIdentityMessage(message, result.message, "info");
    } catch (error) { handleIdentityError(form, message, error); }
    finally { button.disabled = false; }
  });
  const photoForm = document.getElementById("pet-photo-form");
  if (photoForm) photoForm.addEventListener("submit", async event => {
    event.preventDefault(); clearIdentityErrors(photoForm, message);
    const file = photoForm.elements.namedItem("photo").files[0];
    if (!file) { showIdentityMessage(message, "Selecciona una imagen."); return; }
    if (file.size > 5 * 1024 * 1024) { showIdentityMessage(message, "No se admiten imagenes superiores a 5 MB"); return; }
    const button = photoForm.querySelector('button[type="submit"]'); button.disabled = true;
    try {
      const result = await identityRequest(`/account/profile/pets/${id}/photo`, "POST", new FormData(photoForm));
      await load(); showIdentityMessage(message, result.message, "info");
    } catch (error) { handleIdentityError(photoForm, message, error); }
    finally { button.disabled = false; }
  });
}

async function initPreferences(section) {
  const id = section.dataset.id, form = document.getElementById("preferences-form");
  const message = document.getElementById("preferences-error");
  try {
    const current = await identityRequest(`/account/profile/pets/${id}/preferences`);
    for (const group of ["Colors", "Materials", "GarmentTypes", "Styles"])
      form.querySelectorAll(`input[name="${group}"]`).forEach(input => {
        input.checked = current[group[0].toLowerCase() + group.slice(1)].includes(input.value);
      });
  } catch { showIdentityMessage(message, "No se pudieron cargar las preferencias."); }
  form.addEventListener("submit", async event => {
    event.preventDefault(); clearIdentityErrors(form, message);
    const body = {};
    for (const group of ["Colors", "Materials", "GarmentTypes", "Styles"])
      body[group] = [...form.querySelectorAll(`input[name="${group}"]:checked`)].map(input => input.value);
    try {
      await identityRequest(`/account/profile/pets/${id}/preferences`, "PUT", body);
      showIdentityMessage(message, "Preferencias guardadas.", "info");
    } catch (error) { handleIdentityError(form, message, error); }
  });
}

function addressCard(address, reload) {
  const card = element("article", undefined, "panel address-card");
  card.append(element("h3", `${address.province}, ${address.canton}, ${address.district}`),
    element("p", address.exactAddress), element("p", `Referencia: ${address.reference}`));
  if (address.isDefault) card.append(element("strong", "Predeterminada", "state-active"));
  const actions = element("div", undefined, "grupo-botones");
  if (!address.isDefault) {
    const select = element("button", "Marcar predeterminada", "boton secundario"); select.type = "button";
    select.addEventListener("click", async () => {
      try { await identityRequest(`/account/profile/addresses/${address.id}/default`, "POST"); await reload(); }
      catch { alert("No se pudo cambiar la dirección predeterminada."); }
    }); actions.append(select);
  }
  const remove = element("button", "Eliminar dirección", "boton secundario"); remove.type = "button";
  remove.addEventListener("click", async () => {
    if (!confirm("¿Confirmas que deseas eliminar esta dirección?")) return;
    try { await identityRequest(`/account/profile/addresses/${address.id}`, "DELETE"); await reload(); }
    catch { alert("No se pudo eliminar la dirección."); }
  }); actions.append(remove); card.append(actions); return card;
}

async function initAddresses(container) {
  const form = document.getElementById("address-form"), message = document.getElementById("address-error");
  const reload = async () => {
    const addresses = await identityRequest("/account/profile/addresses");
    container.replaceChildren();
    if (!addresses.length) container.append(element("p", "Todavía no tienes direcciones registradas."));
    else addresses.forEach(address => container.append(addressCard(address, reload)));
  };
  try { await reload(); } catch { container.textContent = "No se pudieron cargar las direcciones."; }
  form.addEventListener("submit", async event => {
    event.preventDefault(); clearIdentityErrors(form, message);
    const body = formValues(form); body.IsDefault = form.elements.namedItem("IsDefault").checked;
    try {
      await identityRequest("/account/profile/addresses", "POST", body);
      form.reset(); await reload(); showIdentityMessage(message, "Dirección guardada.", "info");
    } catch (error) { handleIdentityError(form, message, error); }
  });
}
