# Trazabilidad de los criterios de aceptación

Fuente: hoja «Módulo de seguridad y auditoría» de `G2_SC603_K_Requerimientos_CORRECCIONES.xlsx`. Se conservan los identificadores y los cuatro escenarios de cada historia. Los métodos de pruebas se encuentran en `tests/Ameli.Security.Tests/SecurityStoriesTests.cs`; la lógica principal está en `SecurityService.cs`.

| Historia / escenario | Implementación | Verificación automatizada / de interfaz |
| --- | --- | --- |
| SYA-001 / 1 · Inicio exitoso | `LoginAsync`, JWT y redirección `Roles.Home`. | `Login_EmitsValidJwtForEachRole` para tres roles; flujo HTTP de las tres páginas. |
| SYA-001 / 2 · Credenciales inválidas | Mensaje genérico y evento con fecha, hora, origen y correlación. | `UnknownWrongAndInactiveHaveSameResponse`. |
| SYA-001 / 3 · Campos vacíos | HTML `required`, email y longitud; validación de DTO en API. | `EmptyFieldsAreRejectedByApi`; inspección de formulario HTML. |
| SYA-001 / 4 · Inactiva o bloqueada | Estado, rol activo y bloqueo antes de emitir tokens. | `UnknownWrongAndInactiveHaveSameResponse`, `FiveConcurrentFailuresLockWithoutLostUpdates`. |
| SYA-002 / 1 · Solicitud válida | Cola de correo protegida, enlace aleatorio 15 min y respuesta común. | `RecoveryDoesNotDiscloseExistence`, `LinkIsSingleUseAndRevokesExistingTokens`; flujo `.eml`. |
| SYA-002 / 2 · Expirado o usado | Validación al abrir la página y al cambiar; consumo en transacción. | `ResetExpiresAtFifteenMinutes`, `LinkIsSingleUseAndRevokesExistingTokens` con consumo concurrente. |
| SYA-002 / 3 · Contraseña inválida | Política 8–64, mayúscula/minúscula/dígito/símbolo y confirmación en ambos lados. | Contraseña débil en `LinkIsSingleUseAndRevokesExistingTokens`; HTML/JavaScript de formulario. |
| SYA-002 / 4 · Exceso de solicitudes | Límites persistentes por cuenta e IP; no se envían correos extra y se audita. | `AccountAndOriginLimitsDoNotSendExtraLinks`. |
| SYA-003 / 1 · Rol asignado | Cuenta interna activa, rol permitido, revocación y antes/después en evento. | `RoleChangeIsAuditedAndImmediatelyRevokesOldJwt`. |
| SYA-003 / 2 · Acceso de Logística | Autorización del servidor para ruta/página/API y navegación por rol. | `NonAdminCannotReadOrWriteAdminApi`; HTTP a página administrativa. Los futuros módulos deberán aplicar la misma política. |
| SYA-003 / 3 · Último administrador | Restricción transaccional compartida en SQL Server. | `LastAdministratorCannotBeDemotedOrDisabled`, `ConcurrentSelfDemotionsKeepOneAdministrator`. |
| SYA-003 / 4 · Rol no permitido | Rechazo de rol desconocido, inactivo o incompatible; evento. | `RejectsUnknownInactiveAndIncompatibleRoles`. |
| SYA-005 / 1 · Bloqueo automático | Cinco fallos, bloqueo 15 min, revocación y alerta en cola. | `FiveConcurrentFailuresLockWithoutLostUpdates`. |
| SYA-005 / 2 · Desbloqueo automático | Al terminar plazo, autenticación correcta reinicia contador. | Segundo tramo de `FiveConcurrentFailuresLockWithoutLostUpdates`, reloj simulado. |
| SYA-005 / 3 · Éxito antes del límite | Reinicio del contador tras ingreso válido. | `SuccessResetsConsecutiveFailureCounter`. |
| SYA-005 / 4 · Desbloqueo autorizado | Endpoint administrador con motivo y evidencia obligatorios; evento. | `AdminUnlockRequiresEvidenceAndIsAudited`. |
| SYA-006 / 1 · Sesión activa | Actividad explícita, JWT breve y refresh rotativo en servidor web. | `ActivityRenewsIdleDeadlineButPollingAndRefreshDoNot`. |
| SYA-006 / 2 · Inactividad | Deadline SQL a los 15 min, cierre visual y mensaje «Sesión expirada». | `ActivityRenewsIdleDeadlineButPollingAndRefreshDoNot`, `IdleJwtIsRejectedBeforeSideEffects`; JavaScript de sesión. |
| SYA-006 / 3 · Token vencido | JWT bearer valida expiración, firma y sesión antes de ejecutar. | `ExpiredAndTamperedJwtAreRejected`, `IdleJwtIsRejectedBeforeSideEffects`. |
| SYA-006 / 4 · Revocado | Contraseña/estado/cierre global/administrador invalidan sesiones. | `PasswordChangeLogoutAndDeactivationRevokeSessions`, `AdministratorCanRevokeAllSessionsWithReason`, `SessionRevocationCannotTargetAnotherUser`. |

## Límites de la entrega

La matriz implementada cubre seguridad propia para los tres roles y administración de accesos exclusiva del Administrador. Los módulos de productos, proveedores, CRM, BI y pedidos todavía no existen en esta solución: sus desarrolladores deben aplicar los atributos y validaciones documentados al integrarlos. No se afirma que esos módulos estén programados.

Se registran los eventos que estas cinco historias necesitan. El visor/exportación e integridad avanzada de auditoría de SYA-004 y los respaldos de SYA-007 no forman parte de la asignación.

El sistema registra la referencia de la verificación de identidad para un desbloqueo administrativo; la verificación humana debe realizarse antes de usar esa acción. No se inventa ni automatiza una comprobación de identidad externa.
