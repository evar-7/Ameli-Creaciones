document.addEventListener("DOMContentLoaded", function () {
    iniciarMenu();
    iniciarCambioRol();
    iniciarModales();
    iniciarPestanas();
    iniciarFormularios();
    iniciarOpcionesSeleccionadas();
    iniciarFiltros();
    iniciarCatalogo();
    iniciarCarrito();
    iniciarChat();
    iniciarAccionesDemostracion();
});

function iniciarMenu() {
    var boton = document.querySelector(".boton-menu");
    var menu = document.getElementById("menuLateral");

    if (!boton || !menu) return;

    boton.addEventListener("click", function () {
        var abierto = menu.classList.toggle("abierto");
        boton.setAttribute("aria-expanded", abierto ? "true" : "false");
    });

    document.addEventListener("click", function (evento) {
        if (window.innerWidth > 900) return;
        if (!menu.contains(evento.target) && !boton.contains(evento.target)) {
            menu.classList.remove("abierto");
            boton.setAttribute("aria-expanded", "false");
        }
    });
}

function iniciarOpcionesSeleccionadas() {
    document.querySelectorAll(".opcion-seleccionada input, .checklist input").forEach(function (campo) {
        function actualizar() {
            if (campo.type === "radio") {
                document.querySelectorAll("input[name='" + campo.name + "']").forEach(function (opcion) {
                    opcion.closest("label").classList.toggle("seleccionada", opcion.checked);
                });
            } else {
                campo.closest("label").classList.toggle("seleccionada", campo.checked);
            }
        }

        campo.addEventListener("change", actualizar);
        actualizar();
    });
}

function iniciarCambioRol() {
    var selector = document.getElementById("selectorRol");
    if (!selector) return;

    selector.addEventListener("change", function () {
        window.location.href = selector.value;
    });
}

function iniciarModales() {
    var modal = document.getElementById("modalGeneral");
    var titulo = document.getElementById("tituloModal");
    var texto = document.getElementById("textoModal");

    if (!modal) return;

    document.querySelectorAll("[data-modal-titulo]").forEach(function (boton) {
        boton.addEventListener("click", function () {
            titulo.textContent = boton.dataset.modalTitulo || "Formulario";
            texto.textContent = "Complete los campos para visualizar el flujo de esta operación.";
            modal.classList.add("abierto");
            modal.setAttribute("aria-hidden", "false");
            var primerCampo = modal.querySelector("input");
            if (primerCampo) primerCampo.focus();
        });
    });

    document.querySelectorAll("[data-cerrar-modal]").forEach(function (boton) {
        boton.addEventListener("click", cerrarModal);
    });

    document.addEventListener("keydown", function (evento) {
        if (evento.key === "Escape") cerrarModal();
    });

    function cerrarModal() {
        modal.classList.remove("abierto");
        modal.setAttribute("aria-hidden", "true");
    }
}

function iniciarPestanas() {
    document.querySelectorAll("[data-tab]").forEach(function (boton) {
        boton.addEventListener("click", function () {
            var destino = document.getElementById(boton.dataset.tab);
            if (!destino) return;

            var contenedor = boton.closest(".panel") || document;
            contenedor.querySelectorAll("[data-tab]").forEach(function (otroBoton) {
                otroBoton.classList.remove("activa");
            });
            contenedor.querySelectorAll(".contenido-tab").forEach(function (contenido) {
                contenido.classList.remove("activo");
            });

            boton.classList.add("activa");
            destino.classList.add("activo");
        });
    });

    document.querySelectorAll("[data-ver-clave]").forEach(function (boton) {
        boton.addEventListener("click", function () {
            var campo = boton.parentElement.querySelector("input");
            var visible = campo.type === "text";
            campo.type = visible ? "password" : "text";
            boton.textContent = visible ? "Ver" : "Ocultar";
        });
    });
}

function iniciarFormularios() {
    document.querySelectorAll(".formulario-demostracion").forEach(function (formulario) {
        formulario.addEventListener("submit", function (evento) {
            evento.preventDefault();
            formulario.classList.add("validado");

            if (!formulario.checkValidity()) {
                mostrarNotificacion("Revisa los campos obligatorios antes de continuar.");
                return;
            }

            mostrarNotificacion("Información validada. Operación simulada correctamente.");

            var modal = formulario.closest(".modal");
            if (modal) {
                setTimeout(function () {
                    modal.classList.remove("abierto");
                    modal.setAttribute("aria-hidden", "true");
                    formulario.reset();
                    formulario.classList.remove("validado");
                }, 500);
            }
        });
    });
}

function iniciarFiltros() {
    document.querySelectorAll("[data-filtro-tabla]").forEach(function (campo) {
        campo.addEventListener("input", function () {
            var texto = campo.value.trim().toLowerCase();
            var contenedor = campo.closest(".contenido-principal");

            contenedor.querySelectorAll("[data-fila-busqueda]").forEach(function (fila) {
                var contenido = (fila.dataset.filaBusqueda || fila.textContent).toLowerCase();
                fila.style.display = contenido.indexOf(texto) >= 0 ? "" : "none";
            });
        });
    });
}

function iniciarCatalogo() {
    var buscador = document.querySelector("[data-filtro-productos]");
    var categoria = document.querySelector("[data-categoria-producto]");
    var productos = document.querySelectorAll("#listaProductos .tarjeta-producto");
    var vacio = document.getElementById("sinResultados");

    if (!buscador || !productos.length) return;

    function filtrar() {
        var texto = buscador.value.trim().toLowerCase();
        var valorCategoria = categoria ? categoria.value.toLowerCase() : "";
        var visibles = 0;

        productos.forEach(function (producto) {
            var contenido = (producto.dataset.filaBusqueda || producto.textContent).toLowerCase();
            var coincideTexto = contenido.indexOf(texto) >= 0;
            var coincideCategoria = !valorCategoria || contenido.indexOf(valorCategoria) >= 0;
            var mostrar = coincideTexto && coincideCategoria;
            producto.style.display = mostrar ? "" : "none";
            if (mostrar) visibles++;
        });

        if (vacio) vacio.classList.toggle("oculto", visibles > 0);
    }

    buscador.addEventListener("input", filtrar);
    if (categoria) categoria.addEventListener("change", filtrar);
}

function obtenerCarrito() {
    try {
        return JSON.parse(localStorage.getItem("ameli_carrito")) || [];
    } catch (error) {
        return [];
    }
}

function guardarCarrito(carrito) {
    localStorage.setItem("ameli_carrito", JSON.stringify(carrito));
    actualizarContadorCarrito();
    renderizarCarrito();
}

function iniciarCarrito() {
    document.querySelectorAll(".add-to-cart").forEach(function (boton) {
        boton.addEventListener("click", function () {
            var carrito = obtenerCarrito();
            var id = Number(boton.dataset.id);
            var existente = carrito.find(function (item) { return item.id === id; });

            if (existente) {
                existente.cantidad += 1;
            } else {
                carrito.push({
                    id: id,
                    nombre: boton.dataset.name,
                    precio: Number(boton.dataset.price),
                    cantidad: 1,
                    icono: "🐾"
                });
            }

            guardarCarrito(carrito);
            mostrarNotificacion(boton.dataset.name + " se agregó al carrito.");
        });
    });

    var vaciar = document.querySelector("[data-vaciar-carrito]");
    if (vaciar) {
        vaciar.addEventListener("click", function () {
            guardarCarrito([]);
            mostrarNotificacion("El carrito fue vaciado.");
        });
    }

    var puntos = document.querySelector("[data-puntos]");
    if (puntos) {
        puntos.addEventListener("change", function () {
            var total = document.getElementById("totalCheckout");
            if (total) total.textContent = puntos.checked ? "₡25.800" : "₡30.800";
            mostrarNotificacion(puntos.checked ? "Se aplicaron 500 puntos." : "Se retiró el canje de puntos.");
        });
    }

    actualizarContadorCarrito();
    renderizarCarrito();
}

function actualizarContadorCarrito() {
    var carrito = obtenerCarrito();
    var cantidad = carrito.reduce(function (total, item) { return total + item.cantidad; }, 0);
    var contador = document.getElementById("contadorCarrito");
    if (contador) contador.textContent = cantidad;
}

function renderizarCarrito() {
    var contenedor = document.getElementById("contenidoCarrito");
    if (!contenedor) return;

    var carrito = obtenerCarrito();
    var vacio = document.getElementById("carritoVacio");
    var subtotal = carrito.reduce(function (total, item) { return total + item.precio * item.cantidad; }, 0);

    contenedor.innerHTML = "";

    carrito.forEach(function (item) {
        var articulo = document.createElement("article");
        articulo.className = "item-carrito";
        articulo.innerHTML =
            "<span>" + item.icono + "</span>" +
            "<div><strong>" + escaparHtml(item.nombre) + "</strong><p>Producto de demostración</p></div>" +
            "<div class='control-cantidad'><button type='button' data-restar='" + item.id + "'>−</button><span>" + item.cantidad + "</span><button type='button' data-sumar='" + item.id + "'>+</button></div>" +
            "<strong>" + formatoColones(item.precio * item.cantidad) + "</strong>" +
            "<button class='enlace-peligro' type='button' data-eliminar='" + item.id + "' aria-label='Eliminar'>×</button>";
        contenedor.appendChild(articulo);
    });

    if (vacio) vacio.classList.toggle("oculto", carrito.length > 0);
    var subtotalElemento = document.getElementById("subtotalCarrito");
    var totalElemento = document.getElementById("totalCarrito");
    if (subtotalElemento) subtotalElemento.textContent = formatoColones(subtotal);
    if (totalElemento) totalElemento.textContent = formatoColones(subtotal + 2500);

    contenedor.querySelectorAll("[data-sumar]").forEach(function (boton) {
        boton.addEventListener("click", function () { cambiarCantidad(Number(boton.dataset.sumar), 1); });
    });
    contenedor.querySelectorAll("[data-restar]").forEach(function (boton) {
        boton.addEventListener("click", function () { cambiarCantidad(Number(boton.dataset.restar), -1); });
    });
    contenedor.querySelectorAll("[data-eliminar]").forEach(function (boton) {
        boton.addEventListener("click", function () {
            var nuevoCarrito = obtenerCarrito().filter(function (item) { return item.id !== Number(boton.dataset.eliminar); });
            guardarCarrito(nuevoCarrito);
            mostrarNotificacion("El producto se eliminó del carrito.");
        });
    });
}

function cambiarCantidad(id, cambio) {
    var carrito = obtenerCarrito();
    var item = carrito.find(function (producto) { return producto.id === id; });
    if (!item) return;
    item.cantidad += cambio;
    carrito = carrito.filter(function (producto) { return producto.cantidad > 0; });
    guardarCarrito(carrito);
}

function iniciarChat() {
    var formulario = document.getElementById("formChat");
    var campo = document.getElementById("mensajeChat");
    var mensajes = document.getElementById("mensajesChat");
    if (!formulario || !campo || !mensajes) return;

    formulario.addEventListener("submit", function (evento) {
        evento.preventDefault();
        var texto = campo.value.trim();
        if (!texto) return;

        agregarMensaje(texto, "usuario");
        campo.value = "";

        setTimeout(function () {
            var respuesta = "Puedo mostrarte la guía de tallas, los materiales disponibles y productos recomendados. Esta respuesta es una simulación local.";
            if (texto.toLowerCase().indexOf("pedido") >= 0) respuesta = "El pedido AM-1048 se encuentra en preparación y su salida estimada es mañana.";
            if (texto.toLowerCase().indexOf("talla") >= 0) respuesta = "Mide cuello, pecho y largo de espalda. Para Milo, el perfil registrado indica talla M.";
            agregarMensaje(respuesta, "asistente");
        }, 350);
    });

    document.querySelectorAll(".sugerencias-chat button").forEach(function (boton) {
        boton.addEventListener("click", function () {
            campo.value = boton.textContent;
            campo.focus();
        });
    });

    function agregarMensaje(texto, tipo) {
        var mensaje = document.createElement("div");
        mensaje.className = "mensaje " + tipo;
        mensaje.textContent = texto;
        mensajes.appendChild(mensaje);
        mensajes.scrollTop = mensajes.scrollHeight;
    }
}

function iniciarAccionesDemostracion() {
    document.querySelectorAll("[data-accion]").forEach(function (boton) {
        boton.addEventListener("click", function () {
            var accion = boton.dataset.accion;
            var mensajes = {
                exportar: "Se preparó la exportación de ejemplo.",
                cupon: "Cupón de demostración aplicado.",
                recalcular: "Las recomendaciones fueron recalculadas para Milo.",
                calificar: "Gracias. La calificación se reflejó visualmente.",
                descargar: "Se generó el comprobante de ejemplo.",
                guardar: "Las preferencias se guardaron de forma visual.",
                estado: "El estado cambió en la demostración.",
                "cerrar-sesion": "La sesión seleccionada se cerró visualmente.",
                aprobar: "La orden quedó marcada como aprobada.",
                enviar: "La orden quedó marcada como enviada.",
                filtrar: "Los filtros del reporte fueron aplicados.",
                respaldo: "La prueba visual de respaldo inició correctamente.",
                actualizar: "La información operativa fue actualizada.",
                preparado: "La preparación quedó registrada visualmente.",
                "limpiar-chat": "La conversación se limpió.",
            };

            if (accion === "limpiar-chat") {
                var chat = document.getElementById("mensajesChat");
                if (chat) chat.innerHTML = '<div class="mensaje asistente">Conversación nueva. ¿En qué puedo ayudarte?</div>';
            }

            mostrarNotificacion(mensajes[accion] || "Acción demostrada correctamente.");
        });
    });
}

function mostrarNotificacion(texto) {
    var notificacion = document.getElementById("notificacion");
    if (!notificacion) return;
    notificacion.textContent = texto;
    notificacion.classList.add("visible");
    clearTimeout(window.temporizadorNotificacion);
    window.temporizadorNotificacion = setTimeout(function () {
        notificacion.classList.remove("visible");
    }, 2800);
}

function formatoColones(monto) {
    return "₡" + Number(monto).toLocaleString("es-CR", { maximumFractionDigits: 0 });
}

function escaparHtml(texto) {
    var elemento = document.createElement("div");
    elemento.textContent = texto;
    return elemento.innerHTML;
}
