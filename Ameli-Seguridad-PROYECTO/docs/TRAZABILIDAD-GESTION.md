# Desarrollador 2 — 24 escenarios

Fuente: Excel de requisitos, GUI-001 a GUI-004, BIT-001 y SYA-004. Esta tabla describe la implementación; no declara ejecutadas las pruebas SQL pendientes.

| Historia | Escenario | Implementación / prueba |
|---|---|---|
| GUI-001 | Alta válida | API, datos únicos e invitación; CreateAndDuplicatesAreAuditedWithoutExposingClients |
| GUI-001 | Campos obligatorios | Errores por campo y preservación del formulario; InvalidFieldsReturnErrorsAndDoNotCreate |
| GUI-001 | Correo duplicado | Normalización, índice único y enlace a cuenta interna; CreateAndDuplicatesAreAuditedWithoutExposingClients |
| GUI-001 | Teléfono/rol inválido | DTO y validación central; PhoneMustHaveEightAsciiDigits / InvalidFieldsReturnErrorsAndDoNotCreate |
| GUI-002 | Listado autorizado | Solo internos, orden por nombre, última modificación; FilterPaginationEmptyAndInternalPrivacy |
| GUI-002 | Filtros | Nombre/correo, estado y paginación; fetch sin recargar vista |
| GUI-002 | Sin coincidencias | Texto «No se encontraron resultados», resultados anteriores se retiran |
| GUI-002 | Acceso denegado | Autorización en página y API; rechazo web reenviado a auditoría; DeniedRolesCannotReadOrExport |
| GUI-003 | Editar datos válidos | ID estable, snapshots antes/después y revisión; EditRejectsBadDataAndStaleVersionsPreservingIdentity |
| GUI-003 | Datos inválidos | Sin guardar; errores de campos mantienen el formulario |
| GUI-003 | Correo duplicado | Comprobación global incluyendo inactivos, antes de modificar |
| GUI-003 | Edición simultánea | Revisión GUID y bloqueos SQL; HTTP 409 con versión vigente; ConcurrentWritesHaveOneWinner |
| GUI-004 | Desactivar con motivo | Confirmación, estado inactivo, revocación y auditoría; StateChangesRequireConfirmationPreserveHistoryAndRevokeSessions |
| GUI-004 | Reactivar | Mismo ID e historial; confirmación obligatoria |
| GUI-004 | Último administrador | Regla dentro del bloqueo global; pruebas nuevas y prueba concurrente original de SYA-003 |
| GUI-004 | Cancelar | Cierra diálogo sin petición de modificación; comprobar interacción en navegador local |
| BIT-001 | Acceso exitoso | UTC, usuario, rol, IP y sesión; LoginLockoutAndPasswordFailuresHaveMetadataWithoutSecrets |
| BIT-001 | Credenciales inválidas | Resultado Fallido y motivo genérico, sin distinguir credenciales |
| BIT-001 | Bloqueo | Cuenta, contador, inicio/fin, origen y evento de bloqueo |
| BIT-001 | Recuperación/cambio | Éxitos y rechazos sin contraseñas ni tokens; pruebas nuevas y SYA-002 originales |
| SYA-004 | Consulta filtrada | Intervalo, usuario, rol, módulo, acción, resultado, IP y detalle; FiltersAndConfirmedExportHaveIntegrityManifest |
| SYA-004 | Operaciones críticas | Cambios de cuentas/roles, exportaciones y rechazos; extensión compartida para futuros módulos |
| SYA-004 | Exportar | Confirmación, todos los filtros, ZIP, SHA256, manifiesto HMAC y evento del responsable |
| SYA-004 | Integridad | HMAC encadenado + cabecera, diagnóstico y registro; IntegrityDetectsAlterationsAndMissingRows |

La actualización conserva cuentas y marca el historial previo como línea base: prueba UpgradePreservesAccountsAndLegacyHistory. No se implementa un CRUD de inventario: ese módulo debe registrar sus eventos mediante la persistencia auditada compartida descrita en API-GESTION.md.
