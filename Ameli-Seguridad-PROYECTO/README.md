# Ameli Creaciones · Seguridad, gestión interna y auditoría

**Actualización Desarrollador 2:** GUI-001 a GUI-004, BIT-001 y SYA-004, integrada con las historias de acceso anteriores.

Empieza por [ACTUALIZAR-GESTION-AUDITORIA.md](ACTUALIZAR-GESTION-AUDITORIA.md). Incluye instrucciones para Visual Studio sin PowerShell y para conservar tu base y tus secretos.

Pantallas: `/administracion/usuarios`, `/administracion/usuarios/nueva`, `/administracion/usuarios/{id}` y `/administracion/auditoria`. CRUD de cuentas internas con desactivación lógica, concurrencia, filtros y auditoría firmada. La API mantiene las reglas y permisos; la web usa HTML5, CSS y JavaScript sobre Blazor SSR.

Estado de verificación: [VERIFICACION-GESTION.md](docs/VERIFICACION-GESTION.md). Las pruebas de integración nuevas están preparadas, pendientes de ejecución con SQL Server.

---

## Módulo de acceso original

Implementación de **SYA-001, SYA-002, SYA-003, SYA-005 y SYA-006** para el Desarrollador 1. Incluye una solución de Visual Studio con API REST en C# / ASP.NET Core 10, frontend Blazor Web App con HTML5, CSS y JavaScript, Entity Framework Core 10 y SQL Server.

Las vistas conservan la base visual del [prototipo de Ameli](https://evar-7.github.io/Ameli-Creaciones/). El acceso, los permisos, las sesiones y la recuperación se ejecutan contra la API y la base de datos.

## 1. Requisitos

- **Visual Studio 2026, versión 18.0 o posterior**, con la carga «Desarrollo de ASP.NET y web» y SDK .NET 10. Visual Studio 2022, citado en el documento, no admite oficialmente el SDK .NET 10. `global.json` permite usar la versión estable de .NET 10 instalada.
- **SQL Server 2022** o SQL Server Express / LocalDB para desarrollo. Instala LocalDB con Visual Studio si usarás la conexión predeterminada. SQL Server Management Studio es opcional.
- Acceso a NuGet durante la primera restauración, que Visual Studio realiza al abrir la solución.

## 2. Primera ejecución en Windows

**Sigue [INICIAR-EN-VISUAL-STUDIO.md](INICIAR-EN-VISUAL-STUDIO.md): incluye los pasos completos sin PowerShell ni CMD.**

1. Descomprime el proyecto en una carpeta propia y abre **`Ameli.Seguridad.sln`** en Visual Studio.
2. Clic derecho en **Ameli.Api → Administrar secretos de usuario**. Copia la plantilla de la guía y elige el correo del administrador y las contraseñas. Guarda con Ctrl+S.
3. Selecciona el perfil **Ameli - API y Web** o configura **Ameli.Api** y **Ameli.Web** como proyectos de inicio múltiples, ambos en «Iniciar», con perfil `https`.
4. Presiona **F5**. Acepta el certificado HTTPS local de desarrollo si Visual Studio lo solicita. La API prepara la base de datos y las cuentas automáticamente; la web se abre en **https://localhost:7240/ingresar**.

La conexión predeterminada usa LocalDB. Para otra instancia SQL, consulta la guía. La clave JWT de desarrollo se genera automáticamente y se conserva en `src/Ameli.Api/App_Data/development-jwt.key`, fuera de los archivos públicos. Si ya configuraste una clave en los secretos, se conserva esa configuración. La inicialización automática y la generación de esta clave se limitan al ambiente `Development`.

El archivo `scripts/Configurar-Desarrollo.ps1` se conserva como alternativa opcional; no es necesario para este procedimiento.

La API escucha en `https://localhost:7241`; el documento OpenAPI de desarrollo está en `/openapi/v1.json`. `/health` comprueba la conexión con SQL Server.

### Cuentas para la demostración

| Cuenta | Rol | Contraseña |
| --- | --- | --- |
| Correo configurado en los secretos | Administrador | La que elegiste |
| `admin2@ameli.test` | Administrador | Contraseña de prueba elegida |
| `logistica@ameli.test` | Logística | Contraseña de prueba elegida |
| `cliente@ameli.test` | Cliente | Contraseña de prueba elegida |

No se incluyen contraseñas predeterminadas. Usa `Bootstrap:CreateDemoUsers=false` para crear únicamente el administrador. Los siguientes arranques conservan las cuentas, roles y contraseñas existentes. Después de inicializar puedes quitar `Bootstrap:AdminPassword` y `Bootstrap:DemoPassword` de los secretos. Cambiar esos valores no restablece contraseñas existentes; utiliza la recuperación.

### Probar recuperación sin contratar correo

En desarrollo, los correos se generan como archivos **`.eml` legibles en UTF-8 en `src/Ameli.Api/App_Data/mail`**, normalmente dentro de cinco segundos. El cuerpo queda en texto directo (`Content-Transfer-Encoding: 8bit`), sin envolverse en Base64. Abre el mensaje y sigue el enlace de recuperación. También aparecen allí las alertas por bloqueo y las confirmaciones de cambio de contraseña. No se envían mensajes reales en este modo. Para entrega real, configura SMTP según [CONFIGURACION.md](docs/CONFIGURACION.md).

## 3. Historias implementadas

| Historia | Comportamiento |
| --- | --- |
| SYA-001 | Correo y contraseña, hash con sal, JWT, validación de estado, mensajes genéricos y panel según rol. |
| SYA-002 | Solicitud sin revelar cuentas; enlace aleatorio, de un solo uso, válido 15 minutos; contraseña de 8–64 caracteres con mayúscula, minúscula, número y símbolo; límites por cuenta y origen. |
| SYA-003 | Solo Administrador puede gestionar accesos. Al crear cuentas internas asigna una contraseña inicial que se guarda como hash. Roles internos Administrador / Logística para cuentas internas activas. Protección del último administrador, auditoría y revocación al cambiar permisos. |
| SYA-005 | Bloqueo de 15 minutos al quinto fallo consecutivo; correo de alerta; éxito reinicia contador; desbloqueo administrativo con motivo y referencia de verificación de identidad. |
| SYA-006 | Expiración a los 15 minutos de inactividad, renovación por actividad, JWT de 5 minutos, refresh rotativo, cierre actual o global y revocación inmediata por cambios de acceso. |

Consulta la relación de los **20 escenarios del Excel** con el código y las pruebas en [TRAZABILIDAD.md](docs/TRAZABILIDAD.md).

## 4. Organización y decisiones

| Proyecto / carpeta | Responsabilidad |
| --- | --- |
| `src/Ameli.Api` | Controladores REST, reglas de seguridad, EF Core, SQL Server, JWT, correo y migraciones. |
| `src/Ameli.Web` | Blazor con renderizado en servidor, formularios HTML, CSS del prototipo adaptado y seguimiento de actividad. Consume solo la API. |
| `src/Ameli.Contracts` | DTO, validaciones y nombres de roles compartidos. |
| `tests/Ameli.Security.Tests` | Pruebas con SQL Server real y servidor HTTP de pruebas ASP.NET Core. |
| `database/01-security-schema.sql` | Script idempotente generado desde la migración, para revisión o aplicación en SQL Server. |

La API separa dominio, aplicación y persistencia en carpetas para mantener la solución manejable. JSON utiliza camelCase; tablas y columnas SQL, snake_case. `users.role_name` referencia `roles.name`: cada cuenta tiene un rol, como en las historias. `is_internal` impide convertir clientes en personal mediante esta pantalla.

El navegador recibe una cookie de sesión Secure / HttpOnly; **los JWT y refresh tokens permanecen en el servidor web**, sin localStorage. Los formularios usan antiforgery. La API valida firma, emisor, audiencia, vencimiento, rol y sesión persistida en cada solicitud. Contraseñas: `PasswordHasher` de ASP.NET Core con PBKDF2 y 210 000 iteraciones. Tokens de recuperación y renovación: 32 bytes aleatorios; SQL conserva sus hashes SHA-256.

SQL Server coordina operaciones concurrentes con transacciones y `sp_getapplock`: no se pierden fallos y se protege el último administrador entre varias instancias de la API. Los correos se encolan dentro de la transacción; el cuerpo queda protegido con Data Protection y se elimina de la cola tras entregarse. Los eventos no guardan contraseñas ni tokens.

La actividad del usuario se envía a `auth/activity`. Consultar la sesión o renovar el JWT **no extiende** los 15 minutos de inactividad. Existe un límite absoluto adicional de ocho horas. Las mutaciones vuelven a validar sesión y permisos dentro de la transacción. La web comprueba revocaciones cada 30 segundos y al realizar solicitudes.

## 5. Pruebas

En Visual Studio, abre **Prueba → Explorador de pruebas → Ejecutar todas**. Necesitas LocalDB o una conexión de pruebas configurada. También puedes ejecutarlas por consola, opcionalmente:

```powershell
dotnet test tests/Ameli.Security.Tests
```

Con otra instancia SQL, configura una conexión de **pruebas** en `AMELI_TEST_SQL`; debe poder crear y eliminar bases de datos. Cada ejecución crea una base única `AmeliSecurityTests_<guid>` y la elimina al finalizar. No se utiliza `AmeliSecurity` ni se envían correos. Las pruebas simulan el reloj para comprobar los 15 minutos sin esperas reales, pero ejecutan las transacciones sobre SQL Server.

La evidencia de la ejecución de entrega está en [VERIFICACION.md](docs/VERIFICACION.md).

## 6. Integración con el equipo

- Las altas de clientes pertenecen a otros módulos. Las cuentas internas se administran en esta entrega. Deben reutilizar `PasswordHasher<AppUser>`, normalización de correo y los roles existentes; nunca asignar un rol desde un formulario público. En desarrollo, el administrador inicial se crea en el primer arranque con los datos de Bootstrap.
- Para nuevos controladores, aplica `[Authorize]` o `[Authorize(Roles = Roles.Administrator)]` y valida permisos sobre cada recurso. Las acciones sensibles deben seguir el patrón de transacción y revalidación de `SecurityService`.
- Android puede consumir los mismos endpoints; debe almacenar el refresh token en almacenamiento seguro del dispositivo y enviar actividad real. No se incluye una aplicación Android en esta entrega.
- Los paneles de rol incluyen la navegación de este módulo. Pedidos, catálogo, ventas, inventario, backups y SYA-007 quedan fuera de esta entrega. La gestión interna y SYA-004 se incluyen ahora.

## 7. IIS y configuración real

Consulta [CONFIGURACION.md](docs/CONFIGURACION.md) para SQL Server, SMTP, HTTPS, claves persistentes y publicación en IIS. La web usa una caché de sesiones en memoria adecuada al servidor único del documento: reiniciar la web obliga a iniciar sesión nuevamente. Para múltiples instancias web, debe sustituirse por un almacén compartido con exclusión mutua para la rotación de tokens.

No publiques `App_Data/mail`, secretos ni archivos de configuración locales. La API debe conectarse con una cuenta SQL de permisos de ejecución, separada de la cuenta que aplica migraciones. Las bitácoras no tienen endpoints de edición ni eliminación; la retención y administración de respaldo pertenecen a las historias de auditoría/operación.

## Referencias

- Historias: `G2_SC603_K_Requerimientos_CORRECCIONES.xlsx`, hoja Seguridad y auditoría.
- Arquitectura: `G2_SC603_K_Arquitectura-1.pdf`, v1.8: Blazor, ASP.NET Core 10, EF Core y SQL Server 2022.
- [Compatibilidad .NET / Visual Studio](https://learn.microsoft.com/en-us/dotnet/core/install/windows).
- [JWT bearer en ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
- Vistas base del [repositorio Ameli-Creaciones](https://github.com/evar-7/Ameli-Creaciones), referencia `01173d8496fc1f72faf41f4a76e4956c3c584d92`.
