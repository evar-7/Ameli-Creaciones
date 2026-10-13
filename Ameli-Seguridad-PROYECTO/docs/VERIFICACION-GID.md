# Verificación del sprint GID-001 a GID-007

## Al iniciar en Visual Studio

1. Conserva `src/Ameli.Api/App_Data` y los secretos de usuario de tu instalación. Inicia **Ameli.Api** con `Bootstrap:InitializeOnStartup=true`; la migración `CustomerIdentity` se aplica a la base existente. Inicia después **Ameli.Web**.
2. Desde `/registro`, crea un cliente con correo nuevo, nombre, apellidos, teléfono de ocho dígitos y contraseña segura. Comprueba que el inicio de sesión funciona con esa cuenta.
3. Repite el correo con mayúsculas o usa uno ya asignado a un administrador: debe aparecer “Correo electronico ya utilizado, intenta con otro correo” sin crear otra cuenta. Deja campos vacíos: debe señalar cada faltante y conservar lo que ya escribiste.
4. En **Mis mascotas**, comprueba que la lista vacía ofrece Registrar mascota. Registra una mascota y revisa las fechas de registro y modificación. Prueba peso `0`, `-2`, `5 lb`, `abc` y talla `0` o texto: se rechazan con mensajes en cada campo y se conservan los otros valores del formulario.
5. Edita y guarda sin cambiar nada: muestra “No existen cambios por aplicar” y conserva la fecha y revisión. Cambia el peso, una alergia, la foto y el estado activo en el mismo formulario: **Guardar cambios** debe aplicar todo junto y actualizar una vez la fecha. Desactiva y reactiva con los botones separados: la ficha sigue visible en ambos estados.
6. Sube un JPG, PNG o WEBP de hasta 5 MB. Un archivo `.txt`, una extensión engañosa o uno mayor a 5 MB deben rechazarse. La imagen aparece únicamente en la ficha del dueño.
7. Declara `Lana` como alergia; guardar el material `Lana` en Preferencias debe rechazarse. Guardar `Algodón` debe funcionar. Desmarca todas las opciones y guarda: el perfil queda sin preferencias explícitas.
8. Registra dos direcciones, deja referencias vacías para verificar mensajes por campo, marca una como predeterminada y elimina la predeterminada tras confirmar. La otra se promueve automáticamente.
9. Entra con otro cliente y confirma que no puede consultar, modificar, fotografiar ni desactivar mascotas o direcciones de la primera cuenta, incluso usando un ID conocido.

Las pruebas automáticas nuevas están en `tests/Ameli.Security.Tests/CustomerStoriesTests.cs`; desde la solución ejecuta `dotnet test tests/Ameli.Security.Tests`. Requieren SQL Server LocalDB o `AMELI_TEST_SQL`. Este sprint no incluye el checkout, los pedidos ni el motor de recomendaciones; los perfiles inactivos se conservan y las direcciones/preferencias quedan disponibles por API para su integración posterior.
