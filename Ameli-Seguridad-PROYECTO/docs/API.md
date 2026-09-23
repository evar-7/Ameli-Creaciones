# API de seguridad

Base: `https://localhost:7241/api/v1`. JSON camelCase. El documento de desarrollo `/openapi/v1.json` describe los esquemas completos. Los errores de negocio tienen `{ "code": "...", "message": "..." }`; los errores de campos obligatorios usan la respuesta de validación de ASP.NET Core.

| Método y ruta | Acceso | Cuerpo / resultado |
| --- | --- | --- |
| `POST auth/login` | Público | `{email,password}` → JWT, refresh, expiración y estado. |
| `POST auth/forgot-password` | Público | `{email}` → 202 con el mismo mensaje público. |
| `POST auth/validate-reset` | Público | `{userId,token}` → enlace vigente o error genérico; no consume el token. |
| `POST auth/reset-password` | Público | `{userId,token,password,confirmPassword}` → cambio y revocación global. |
| `POST auth/refresh` | Público con refresh válido | `{refreshToken}` → nuevo JWT y nuevo refresh. El anterior deja de servir. |
| `GET auth/session` | Autenticado | Usuario y vigencia de la sesión actual. No renueva inactividad. |
| `POST auth/activity` | Autenticado | Sin cuerpo; registra actividad y devuelve nueva vigencia. |
| `GET auth/sessions` | Autenticado | Solo sesiones propias vigentes. |
| `POST auth/logout` | Autenticado | Revoca la sesión actual, 204. |
| `POST auth/logout-all` | Autenticado | Revoca todas las sesiones propias, 204. |
| `DELETE auth/sessions/{id}` | Autenticado | Revoca una sesión propia; una sesión ajena responde 404. |
| `POST auth/change-password` | Autenticado | `{currentPassword,password,confirmPassword}` → cambio y revocación global. |
| `GET admin/users?q=...` | Administrador | Busca cuentas por nombre/correo, máximo 200 resultados. |
| `PUT admin/users/{id}/role` | Administrador | `{role}`: Administrador o Logística en cuenta interna activa. |
| `PUT admin/users/{id}/status` | Administrador | `{isActive,reason}`; motivo de 5–500 caracteres. |
| `POST admin/users/{id}/unlock` | Administrador | `{reason,identityEvidence}`; ambos de 5–500 caracteres. |
| `POST admin/users/{id}/revoke-sessions` | Administrador | `{reason}`; revoca todas las sesiones de esa cuenta. |

Para endpoints autenticados: `Authorization: Bearer <accessToken>`. Una respuesta 401 requiere renovar el JWT con el refresh válido o volver a iniciar sesión; 403 indica falta de permiso y no se resuelve intentando cambiar el rol en el cliente. Un cambio de rol exige nueva autenticación, nunca editar las claims localmente.

La sesión caduca a los 15 minutos de inactividad y tiene un máximo absoluto de ocho horas. Envía `activity` por interacción real; no envíes latidos automáticos para mantenerla viva. La consulta pasiva y la renovación del JWT no cuentan como actividad.

El frontend Blazor mantiene ambos tokens en el servidor y usa su cookie HttpOnly y antiforgery para los formularios `/account/*`. No copies esos endpoints de formulario al cliente Android: usa la API REST.

Los enlaces de recuperación incluyen un token secreto. No registres cuerpos de login/refresh/reset ni cadenas de consulta de `/restablecer` en IIS, proxies, telemetría o herramientas de captura. La aplicación suprime los logs de solicitudes ASP.NET de nivel Information y usa `Referrer-Policy: no-referrer`, pero el registro del servidor frontal se configura por separado.
