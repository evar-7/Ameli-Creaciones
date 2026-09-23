# Configuración de desarrollo y despliegue

## Configurar desde Visual Studio

La guía [INICIAR-EN-VISUAL-STUDIO.md](../INICIAR-EN-VISUAL-STUDIO.md) explica la primera ejecución con ventanas de Visual Studio y F5; no requiere terminal.

En Visual Studio, clic derecho en **Ameli.Api → Administrar secretos de usuario**. Completa este esquema con valores elegidos para tu entorno; los marcadores entre `< >` deben reemplazarse:

```json
{
  "ConnectionStrings:DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=AmeliSecurity;Trusted_Connection=True;TrustServerCertificate=True",
  "Bootstrap:AdminEmail": "<correo del administrador>",
  "Bootstrap:AdminPassword": "<contraseña elegida de 8-64 caracteres>",
  "Bootstrap:CreateDemoUsers": false,
  "Email:Mode": "Pickup"
}
```

En `Development`, al presionar F5, la API aplica las migraciones y crea las cuentas que falten. Necesita permiso para crear la base y modificar su esquema. Para cuentas de prueba, establece además `Bootstrap:CreateDemoUsers=true` y `Bootstrap:DemoPassword` antes de iniciar. La inicialización usa una transacción y un bloqueo SQL para evitar cuentas parciales o duplicadas entre arranques simultáneos. No reemplaza contraseñas ni roles existentes.

Después de inicializar puedes eliminar `Bootstrap:AdminPassword` y `Bootstrap:DemoPassword` de los secretos. Si eliminas la base de desarrollo, vuelve a configurarlos antes de crearla otra vez.

Si `Jwt:SigningKey` no está configurada, únicamente en `Development` se generan 48 bytes aleatorios y se conserva la clave en `src/Ameli.Api/App_Data/development-jwt.key`. No borres ni compartas ese archivo; está excluido del repositorio y de la entrega. Si ya hay una clave explícita, tiene prioridad. User Secrets y esta clave local son configuración de desarrollo, no un almacén de secretos de producción. La web no necesita la clave JWT.

En producción configura explícitamente `Jwt:SigningKey` con al menos 32 bytes aleatorios mediante un almacén protegido. No se genera ni se carga la clave local y no se ejecuta la inicialización automática. El modo de instalación `--initialize` continúa disponible para aplicar migraciones y crear el administrador con una identidad de instalación y datos de Bootstrap configurados. El script alternativo `database/01-security-schema.sql` aplica el esquema en una base creada para Ameli; no crea cuentas. EF reconoce una migración ya aplicada por ese script.

## SMTP

En User Secrets durante pruebas controladas, o en la configuración protegida de producción:

```json
{
  "Email:Mode": "Smtp",
  "Email:From": "<remitente autorizado>",
  "Email:Host": "<servidor SMTP>",
  "Email:Port": 587,
  "Email:Username": "<usuario SMTP>",
  "Email:Password": "<secreto SMTP>",
  "Security:WebBaseUrl": "https://<dominio-web>"
}
```

El adaptador usa SMTP con STARTTLS; configura un proveedor compatible con ese método y con autenticación de aplicación. Los proveedores que exigen OAuth necesitan otro adaptador `IEmailDelivery`. El puerto 465 con TLS implícito no es el transporte de este adaptador.

`Pickup` escribe `.eml` en `App_Data/mail` solo en `Development` o `Testing`. Nunca publiques esa carpeta. Los enlaces y los mensajes son datos de seguridad y deben quedar accesibles únicamente al operador de desarrollo.

La cola reintenta entregas fallidas hasta cinco intentos. Consulta `email_outbox` para identificar pendientes o fallidos y `security_events` para la evidencia. Si el servicio se interrumpe después de entregar y antes de confirmar la transacción, un correo puede repetirse; el enlace sigue siendo de un solo uso. No hay un panel de operación de correo en estas historias.

## Opciones principales

| Clave | Valor de entrega | Uso |
| --- | --- | --- |
| `Security:FailedAttempts` | 5 | Fallos consecutivos antes del bloqueo. |
| `Security:LockoutMinutes` | 15 | Duración del bloqueo. |
| `Security:ResetMinutes` | 15 | Vida del enlace de recuperación. |
| `Security:IdleMinutes` | 15 | Máximo sin actividad real. |
| `Security:AccessTokenMinutes` | 5 | Vida de cada JWT. |
| `Security:AbsoluteSessionHours` | 8 | Límite absoluto incluso con actividad. |
| `Security:RecoveryAccountLimit` | 3 | Solicitudes por cuenta cada 15 minutos. |
| `Security:RecoveryOriginLimit` | 10 | Solicitudes por IP cada 15 minutos. |
| `Security:WebBaseUrl` | `https://localhost:7240` | Origen de los enlaces, nunca tomado del encabezado Host. |
| `Api:BaseUrl` (web) | `https://localhost:7241/api/v1/` | API consumida por la web. Debe terminar en `/`. |
| `DataProtection:Directory` | `App_Data/keys` por aplicación | Claves persistentes de cookies o cola. |
| `Bootstrap:InitializeOnStartup` | `true` solo en Development | Aplica migraciones y crea cuentas faltantes al iniciar la API. |

Los valores 5/15 forman parte de las historias. Cambiarlos requiere acordar el criterio con el equipo. Como defensa adicional, la API limita por IP a 60 solicitudes/minuto los endpoints de autenticación y a 12/minuto la recuperación; sin cola de espera. Los límites de recuperación por cuenta/origen quedan en SQL Server y conservan respuesta pública genérica. El límite HTTP adicional devuelve 429; la web mantiene la misma confirmación de recuperación.

## Publicar en IIS

1. Instala IIS y el **ASP.NET Core Hosting Bundle de .NET 10**. Publica los proyectos `Ameli.Api` y `Ameli.Web` en carpetas distintas (`dotnet publish ... -c Release`). Usa pools separados con identidades restringidas y «Sin código administrado».
2. Configura bindings HTTPS con certificados válidos. La web y la API tienen URL propia; configura `Api:BaseUrl` y `Security:WebBaseUrl`. No expongas SQL Server a Internet. Conserva `ASPNETCORE_ENVIRONMENT=Production`.
3. Usa configuración externa al repositorio para la conexión SQL, la clave JWT y SMTP. Las variables usan doble guion bajo, por ejemplo `Jwt__SigningKey`. `appsettings.Local.json`, si se usa, se carga al final y tiene prioridad; no lo incluyas en el control de versiones ni en paquetes compartidos. User Secrets es exclusivamente para desarrollo.
4. Reemplaza `AllowedHosts` de ambas aplicaciones por sus nombres de host reales. Usa cifrado de SQL con certificado válido; `TrustServerCertificate=True` se incluye solo para LocalDB y pruebas locales.
5. Aplica las migraciones con una identidad de instalación. La identidad de ejecución necesita SELECT en roles, SELECT/INSERT/UPDATE en usuarios/sesiones/tokens/cola, SELECT/INSERT en intentos de recuperación y únicamente INSERT en bitácoras de seguridad; no necesita crear bases ni modificar esquemas. El ejemplo de pruebas tiene permisos mayores porque crea y elimina su base aislada.
6. Mantén `DataProtection:Directory` persistente, separado para API y web, con ACL para su propia identidad. En Windows las claves se protegen con DPAPI. Configura «Cargar perfil de usuario» en los pools y respalda las claves junto con el procedimiento de operación. Cambiar de identidad sin conservar acceso a sus claves puede invalidar cookies o impedir leer correos pendientes.
7. El servicio de correo corre dentro de la API: configura el pool con inicio permanente y evita que IIS lo suspenda por inactividad. Revisa los eventos de entregas fallidas. La web en memoria pierde sesiones al reciclar su proceso; los usuarios podrán autenticarse de nuevo.

La API solo confía por defecto en encabezados de proxy provenientes del loopback. Si separas los servidores, configura explícitamente los proxies confiables y la cadena de IP; no aceptes `X-Forwarded-For` de cualquier origen. El servidor web envía a la API la IP observada del cliente para los límites y la auditoría.

La arquitectura del documento usa un servidor único. Un despliegue con varias instancias web necesita un almacén de sesión distribuido y coordinación de refresh; no basta con compartir la clave de cookies. La API sí coordina las reglas de concurrencia en SQL Server.

## Resolver problemas frecuentes

| Síntoma | Qué revisar |
| --- | --- |
| No encuentra .NET 10 | Instala VS 2026 con SDK .NET 10 y abre de nuevo la solución. |
| No conecta con SQL | Instancia iniciada, nombre de servidor, autenticación y permiso para crear la base en la inicialización. |
| API no arranca por SigningKey | En Development, comprueba permisos de escritura en `App_Data` o una clave explícita inválida en los secretos. En producción, configura `Jwt:SigningKey`. |
| Web devuelve servicio no disponible | Espera a que la API termine de preparar SQL, comprueba ambos proyectos de inicio y acepta el certificado HTTPS local cuando Visual Studio lo solicite. |
| No llega el correo durante desarrollo | Abre los `.eml` de `src/Ameli.Api/App_Data/mail`; el modo predeterminado de desarrollo no envía SMTP. |
| Se cerró al cambiar un rol/contraseña/estado | Es el comportamiento de revocación requerido; vuelve a autenticarte. |
| Pruebas no conectan en Linux | Define `AMELI_TEST_SQL` con una instancia SQL Server; LocalDB solo está disponible en Windows. |

Referencias oficiales: [Secretos de desarrollo](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0), [IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0), [Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
