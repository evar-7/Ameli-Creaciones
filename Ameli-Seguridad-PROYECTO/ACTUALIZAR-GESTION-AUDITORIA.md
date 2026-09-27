# Actualizar en Visual Studio — Desarrollador 2

Esta entrega integra GUI-001, GUI-002, GUI-003, GUI-004, BIT-001 y SYA-004 con el módulo de acceso anterior. No requiere PowerShell.

## Si ya tienes la versión anterior funcionando

1. Detén los proyectos y conserva una copia de tu carpeta anterior. Respalda `AmeliSecurity` desde SQL Server Management Studio: clic derecho en la base → Tareas → Copia de seguridad.
2. Extrae este ZIP en otra carpeta. Copia las carpetas `src/Ameli.Api/App_Data` y `src/Ameli.Web/App_Data` de tu proyecto anterior a las mismas ubicaciones de la nueva carpeta, si existen. Conservan claves y mensajes pendientes.
3. Abre `Ameli.Seguridad.sln`. Los identificadores de secretos de usuario se mantienen. En **Ameli.Api → Administrar secretos de usuario**, conserva tu conexión SQL y tus valores existentes. No reemplaces el archivo de secretos por una plantilla vacía.
4. Configura **Ameli.Api** y **Ameli.Web** como proyectos de inicio, con perfil **https** y ambiente **Development**, igual que antes. Usa **Ctrl+F5** para ejecutar sin el depurador.
5. La API aplica automáticamente la migración nueva al arrancar. Espera el mensaje «Base de datos de desarrollo preparada». No borres la base ni vuelvas a crear usuarios.
6. Ingresa como administrador. En el menú aparecen **Cuentas internas** y **Auditoría**.

La clave de conexión que lee el proyecto sigue siendo `ConnectionStrings:DefaultConnection`. Si tu instancia es SQL Express, un ejemplo con autenticación de Windows es:

```json
"ConnectionStrings:DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=AmeliSecurity;Trusted_Connection=True;TrustServerCertificate=True"
```

Conserva la conexión que ya te funciona. Las cuentas existentes mantienen ID, contraseña, rol e historial. El teléfono anterior aparece como «Por completar»: al editar, escribe sus 8 dígitos.

## Cuentas internas

- **Nueva cuenta:** nombre, correo único, teléfono de 8 dígitos, Administrador o Logística y estado. Las cuentas activas reciben un enlace para elegir contraseña, válido 15 minutos. En Development aparece como `.eml` en `src/Ameli.Api/App_Data/mail`; no se envía un correo real salvo que configures SMTP.
- **Consultar:** filtra por nombre/correo y estado; limpiar filtros y cambiar de página no recarga toda la vista.
- **Editar:** los errores conservan los datos escritos. Un correo duplicado permite abrir la cuenta interna existente. Los datos de clientes no se muestran en este módulo.
- **Activar/desactivar:** cambia Estado, escribe un motivo, guarda y confirma «¿Está seguro?». Cancelar el diálogo no envía el cambio. La desactivación conserva historial y cierra sesiones.
- **Ediciones simultáneas:** el servidor devuelve la versión vigente; «Cargar versión vigente» exige revisar y volver a aplicar cambios. No se sobrescribe silenciosamente.
- El último administrador activo no puede desactivarse ni perder el rol.

## Auditoría

Filtra por intervalo UTC, usuario, rol, módulo, acción, resultado e IP. Cada detalle muestra entidad/ID, sesión, correlación, datos anteriores/posteriores y metadatos del bloqueo cuando corresponda. Solo el administrador puede consultar, exportar o verificar.

**Exportar registros** pide confirmación y descarga un ZIP con `eventos.json` y `manifest.json`: filtros, responsable, fecha, SHA-256 del archivo y HMAC del manifiesto. El límite es 10 000 eventos por archivo; reduce los filtros si lo superas. La exportación se registra.

**Verificar integridad** revisa la cadena completa para detectar alteraciones y registros faltantes, mostrando también cuántos coinciden con el rango elegido. Registra el resultado. Si la propia cabecera está dañada, devuelve el diagnóstico y emite una alerta en el registro operativo, sin reinicializar la cadena.

## Claves y actualización SQL

En Development se genera una clave persistente nueva en `src/Ameli.Api/App_Data/audit-integrity.key`. Consérvala junto con las claves anteriores. Si se pierde o cambia, las firmas de la auditoría dejan de verificarse. En producción configura `Audit:IntegrityKey` con al menos 32 bytes aleatorios en el almacén de secretos y la misma clave en todas las instancias.

La migración agrega teléfono, revisión de edición, última modificación, metadatos de auditoría y cabecera firmada. El historial anterior se incorpora como línea base marcada `IsLegacy`: no demuestra su integridad antes de esta actualización. Una restauración completa de una copia antigua de SQL y su cabecera requiere contrastar con exportaciones o respaldos externos para detectarse.

`database/02-internal-accounts-audit.sql` contiene la actualización idempotente, para revisión o ejecución manual sobre una base que ya tiene la primera migración. La ejecución normal desde Visual Studio la aplica automáticamente. No ejecutes la migración inversa sobre datos que quieras conservar.

## Verificación de esta entrega

Consulta `docs/VERIFICACION-GESTION.md`. La compilación y las pruebas sin SQL se verificaron aquí. Las pruebas de integración están incluidas; quedan pendientes en una instancia SQL Server porque el motor del entorno de trabajo no pudo arrancar. En Visual Studio usa **Prueba → Explorador de pruebas → Ejecutar todas** con LocalDB instalado o una conexión de pruebas configurada.

Los flujos nuevos necesitan JavaScript habilitado. Si Visual Studio pausa en una excepción de negocio al probar datos incorrectos, usa Ctrl+F5; el servidor convierte esos rechazos en respuestas HTTP y mensajes del formulario.
