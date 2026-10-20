# Corrección del carrito — 20 de octubre de 2026

Esta actualización parte del ZIP del equipo con la página Recomendados y conserva la referencia corregida a `System.Security.Cryptography.Pkcs` 9.0.21. No requiere una migración nueva ni cambiar SMTP o User Secrets.

## Comportamiento

- Catálogo y ficha: un cliente puede seleccionar talla y cantidad, agregar productos y abrir su carrito.
- Sin tabla publicada: la compra manual exige marcar «Quiero comprarlo sin talla especificada». El servidor exige esta confirmación y guarda la talla vacía; carrito y pedidos muestran «Sin talla especificada». Esta compra no se vincula a una mascota ni afirma compatibilidad biométrica.
- Con tabla publicada: hay que seleccionar una talla de esa tabla. La confirmación anterior no permite omitirla ni inventarla.
- Chat y Recomendados: ambos permiten agregar. Una recomendación conserva la mascota y la talla sugerida; la API vuelve a validar propietario, perfil activo, medidas, alergias, especie y stock.
- Sin stock: no se ofrece agregar. La API también controla la cantidad acumulada y la disponibilidad antes de confirmar el pedido.
- Reintentos: mientras se agrega, los controles se bloquean. Ante una desconexión o HTTP 5xx se conserva el mismo identificador y selección. Un rechazo de negocio permite corregir los datos; después de una compra confirmada, «Agregar más» inicia una nueva operación.
- Si el catálogo cambia antes de confirmar, el servidor vuelve a validarlo. Un producto que ahora tiene tabla requiere seleccionar una talla; un producto inactivo o sin stock impide confirmar.
- Los archivos JavaScript modificados incluyen una nueva versión en su URL para renovar la caché del navegador.

## Verificación de esta actualización

| Comprobación | Resultado |
| --- | --- |
| Sintaxis de los siete archivos JavaScript de la web | Correcta. |
| `node --test tests/cart-ui.test.cjs` | 10 pruebas aprobadas. Ejecutan el JavaScript entregado con adaptadores de formularios y transporte. |
| XML de proyectos y `Directory.Build.props` | Válido. |
| Compilación C# y nuevas pruebas API con SQL | Pendientes: el entorno de edición no tiene el SDK .NET y su descarga está bloqueada. |
| Recorrido en navegador con API/SQL reales | Pendiente en esta actualización. |

Los resultados `.trx` y la verificación AAC anteriores corresponden a entregas anteriores; no constituyen una ejecución del código de esta actualización.

`CartPurchaseTests` añade 15 casos de integración: consentimiento, carrito y pedido idempotentes, tallas obligatorias, cantidades y stock, recomendaciones sin tabla, propiedad/alergias/ambigüedad, cambios de catálogo antes del pedido y autorización. Se conserva también el recorrido AAC anterior y se adapta su comprobación del botón de compra.

## Repetir comprobaciones

Desde la carpeta de la solución, con SDK .NET 10 y SQL Server LocalDB disponibles:

```powershell
dotnet restore Ameli.Seguridad.sln
dotnet build Ameli.Seguridad.sln --no-restore
dotnet test tests/Ameli.Security.Tests --no-restore --filter "FullyQualifiedName~CartPurchaseTests|FullyQualifiedName~AssistantCommerceTests"
node --test tests/cart-ui.test.cjs
```

La fixture de integración crea y elimina una base de pruebas con nombre aleatorio; requiere permisos para hacerlo. Para otra instancia de SQL, configura `AMELI_TEST_SQL` con una conexión de pruebas.

## Prueba desde Visual Studio

1. Restaura los paquetes e inicia API y web.
2. Inicia sesión con una cuenta de cliente y abre Catálogo.
3. Selecciona talla y cantidad. Si el producto no tiene tabla, marca la confirmación de compra sin talla.
4. Agrega, entra a Mi carrito y selecciona una dirección propia. Si falta una dirección, regístrala desde Administrar mis direcciones.
5. Confirma el pedido y revisa Mis pedidos. Repite la compra desde una recomendación del chat y desde Recomendados.

La confirmación registra el pedido con el flujo existente. Esta aplicación todavía no integra cobros electrónicos.
