# Sprint AAC-001 a AAC-005

## Actualizar la instalación

1. Copia el código de esta versión sobre tu proyecto conservando `src/Ameli.Api/App_Data`, `src/Ameli.Web/App_Data` y los secretos de usuario. Los identificadores de User Secrets siguen siendo los mismos.
2. Inicia **Ameli.Api** en Development con `Bootstrap:InitializeOnStartup=true` y después **Ameli.Web**. La migración `AssistantCommerce` amplía el esquema y conserva las cuentas, productos y perfiles. Los SKU existentes se generan antes de crear el índice único. No borres la clave de auditoría ni la base para actualizar.
3. Desde Administración → Productos base → **Configurar ficha y tallas**, completa SKU, materiales, cuidados, colores, prenda, estilo y rangos reales de altura a la cruz (cm) y peso (kg). Las tablas pueden quedar vacías; en ese caso no se calculan tallas ni se recomiendan tallas inventadas.
4. En el perfil de la mascota registra alergias conocidas o marca que confirmas que no tiene alergias conocidas. Las mascotas antiguas con alergias escritas quedan declaradas al migrar; las que estaban en blanco requieren confirmación.

## Escenarios y comportamiento

| Historia / escenario | Comprobación |
| --- | --- |
| AAC-001.1 | Agrega un producto al carrito, registra una dirección y confirma un pedido. Copia el número desde Mis pedidos; el chat responde con estado, última actualización, transportista y guía si existen. Administración o Logística pueden actualizar el seguimiento. |
| AAC-001.2 | Desde otra cuenta consulta el mismo número y luego uno inexistente: ambos reciben el mismo rechazo, sin datos del pedido. |
| AAC-001.3 | Como visitante solicita un pedido o incluye correo/teléfono: se exige iniciar sesión; esos datos no prueban identidad. |
| AAC-002.1 | Elige Guía de tallas, producto, especie, raza, altura (cm) y peso (kg). Si una sola fila de su tabla coincide, muestra la sugerencia, equivalencias y guía visual del producto. |
| AAC-002.2 | Omite especie, raza, altura o peso, o escribe cero, negativo, texto o una unidad incorrecta. Se identifica cada campo con la unidad correspondiente y no se calcula una talla. |
| AAC-002.3 | Configura rangos solapados o prueba medidas entre tallas/fuera de rango. El chat no elige arbitrariamente: explica la ambigüedad y pide revisar las medidas y el ajuste. |
| AAC-003.1 | Consulta nombre o SKU activo. La respuesta refleja precio, tallas, materiales, cuidados y stock de SQL Server y enlaza al producto. Los datos sin publicar se muestran como faltantes. |
| AAC-003.2 | Deja stock cero y consulta el producto. Muestra Agotado y alternativas activas de la misma categoría con stock; si no hay alternativas, no las fabrica. |
| AAC-003.3 | Pregunta sobre un tema ajeno a productos, tallas, recomendaciones o pedidos. El chat indica su alcance y las funciones disponibles. |
| AAC-004.1 | Selecciona una mascota propia activa con medidas y alergias declaradas. El motor local filtra y ordena productos; las tarjetas muestran talla sugerida, motivo, stock y enlace. |
| AAC-004.2 | Deja alergias sin declarar o usa un perfil incompleto/inactivo. Se detallan los datos pendientes y se enlaza al editor del perfil propio. No se admiten perfiles ajenos. |
| AAC-004.3 | El enunciado solo incluía el título «Agregar desde el chat». Se implementó agregar producto/talla al carrito del cliente: vuelve a validar stock, propiedad y alergias, y evita duplicados ante reintentos. |
| AAC-005.1 | Abre el chat a 390×844, 768×1024, 1280×800 y 812×375. Mensajes desplazables, campo, envío, cerrar y tamaño del texto permanecen accesibles. Enter envía, Shift+Enter inserta línea, Escape cierra. |
| AAC-005.2 | Retrasa una respuesta más de 4 segundos. Aparece el aviso de que sigue procesando y Cancelar consulta. Cancelar aborta la solicitud y descarta cualquier respuesta tardía; reintentar reutiliza la consulta sin duplicar mensajes. |
| AAC-005.3 | Sin Internet, envía texto: se conserva en `sessionStorage`, se informa que no fue enviado y aparece Reintentar. Al volver la conexión se permite enviarlo; el borrador se elimina al responder o cerrar sesión. |

## Motor, carrito y pedidos

- Recomendador local basado en contenido: similitud de coseno de colores, materiales, prenda y estilo. Primero se filtran especie, medidas, materiales alérgicos y stock. No consulta un modelo generativo externo ni requiere credenciales nuevas.
- Las recomendaciones no incluyen productos sin material o tabla publicada. No se garantiza compatibilidad absoluta: la guía invita a verificar el ajuste y cualquier medida adicional de la prenda.
- Un perfil sin preferencias usa medidas, alergias y stock; esta versión no incorpora una fuente de tendencias externas.
- El stock mostrado es el total del producto. No se inventan cantidades por talla. Agregar al carrito no reserva inventario; confirmar el pedido vuelve a validar y descuenta dentro de una transacción.
- Carrito y confirmación usan identificadores de solicitud y bloqueos SQL para no duplicar cantidades/pedidos en reintentos. La confirmación registra el pedido; no integra una pasarela de pago.
- Al registrar el pedido se copian producto/precio, perfil de mascota y dirección. Las copias sobreviven a cambios del perfil y a eliminaciones de direcciones. El chat solo devuelve estado y seguimiento del pedido propio.
- Estados: Confirmado → En preparación/Enviado/Cancelado; En preparación → Enviado/Cancelado; Enviado → Entregado. Cancelar antes del envío devuelve stock una vez. No permite retroceder un pedido entregado.

## Pruebas ejecutadas para esta entrega

- Compilación de la solución con .NET 10: **0 errores y 0 advertencias**.
- Suite de C#: **82 pruebas aprobadas**, sin omisiones, con SQL Server de prueba independiente. Incluye seguridad anterior, perfiles y nuevas consultas/operaciones AAC.
- Prueba de navegador AAC-005: **aprobada** con tamaños móvil/tableta/escritorio, demora, cancelación, deduplicación y recuperación sin conexión. En esta prueba se simulan respuestas de red para controlar la demora y desconexión.
- Recorrido de navegador conectado a API y SQL Server reales: **aprobado** desde registro y perfil hasta recomendación, carrito, confirmación y consulta de transportista/guía actualizados.

## Repetir pruebas

Desde la solución:

```text
dotnet test tests/Ameli.Security.Tests
```

Usa SQL Server LocalDB en Windows o configura `AMELI_TEST_SQL` con una conexión de pruebas. La fixture crea una base separada con nombre aleatorio y la elimina al terminar. Las pruebas no envían correo.

Para probar solo reglas y consultas sin SQL Server:

```text
dotnet test tests/Ameli.Security.Tests --filter "FullyQualifiedName~AssistantRulesTests|FullyQualifiedName~AssistantReadTests"
```

Pruebas de navegador opcionales: en `tests/browser` ejecuta `npm install` y `npx playwright install chromium`. Con la web iniciada y `AMELI_WEB_URL` configurado a su dirección HTTPS, ejecuta `npm test`. También puedes indicar una ruta de Chrome existente en `AMELI_BROWSER_EXECUTABLE`.

`assistant-e2e.spec.cjs` es un recorrido que crea datos: ejecútalo únicamente sobre una instalación de prueba, con `AMELI_API_URL`, `AMELI_WEB_URL`, `AMELI_E2E_ADMIN_EMAIL` y `AMELI_E2E_ADMIN_PASSWORD`. No almacenes contraseñas en el código. Ejecuta `node --test assistant-e2e.spec.cjs`.
