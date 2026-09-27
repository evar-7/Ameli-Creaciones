# API de gestión y auditoría

Todos los endpoints de administración requieren JWT válido, sesión vigente y rol Administrador. La web los consume mediante su servidor BFF, con cookie HttpOnly y protección antiforgery para escrituras. No almacena JWT en el navegador.

| Método | Ruta relativa a `/api/v1` | Uso |
|---|---|---|
| GET | `/admin/internal-accounts` | Listar: search, isActive, page, pageSize |
| GET | `/admin/internal-accounts/{id}` | Leer una cuenta interna |
| POST | `/admin/internal-accounts` | Crear cuenta interna con contraseña inicial asignada por el administrador |
| PUT | `/admin/internal-accounts/{id}` | Editar con Revision vigente |
| PUT | `/admin/internal-accounts/{id}/state` | Cambiar estado, motivo y confirmación |
| GET | `/admin/audit` | Consultar eventos filtrados |
| POST | `/admin/audit/export` | Exportar filtros con Confirmed=true |
| POST | `/admin/audit/verify` | Comprobar cadena firmada |
| POST | `/auth/access-denied` | Registrar rechazo en la web para el propio usuario autenticado |

Alta: `name`, `email`, `phone`, `role`, `isActive`, `password`, `confirmPassword`. La contraseña inicial cumple la misma política de seguridad que el cambio/restablecimiento y se persiste únicamente como hash. Edición agrega `revision`, `reason` y `confirmed`, y acepta opcionalmente `password` y `confirmPassword`: si se omiten, conserva la contraseña actual; si se envían, ambos deben cumplir la política y coincidir. Cambiar la contraseña invalida sesiones y recuperaciones pendientes. Estado usa `revision`, `isActive`, `reason` y `confirmed`. El correo es único globalmente, incluso para cuentas inactivas. Si pertenece a un cliente, no se devuelve su ID ni sus datos.

Filtros de auditoría: `from`, `to` (YYYY-MM-DD, límites de día UTC inclusivos), `user` (nombre/correo/ID), `role`, `module`, `action`, `outcome`, `ip`, `page`, `pageSize`. Máximo 100 resultados por página. Exportación incluye todos los resultados filtrados hasta 10 000; no trunca silenciosamente.

Errores: `code`, `message`, `errors` por campo; duplicados internos incluyen `existingId`; conflictos de edición devuelven HTTP 409 con `current`. El cliente debe revisar la versión vigente y enviar su nueva revisión explícitamente.

No hay eliminación física de cuentas ni endpoints para editar/eliminar auditoría. Las rutas previas `/admin/users` se conservan para compatibilidad de seguridad; su listado muestra únicamente personal interno. El nuevo CRUD no expone clientes.

Para futuros módulos (incluido inventario), añade `SecurityEvent` con actor, módulo, entidad, resultado y snapshots de campos permitidos dentro de la misma transacción que la operación y usa `SaveChangesAsync`. La persistencia asigna ID y firma bajo un bloqueo SQL compartido por todas las instancias. No uses SQL directo ni ExecuteUpdate/Delete para alterar auditoría. El módulo de inventario no forma parte de este encargo.

SHA-256 permite comprobar los bytes de `eventos.json`; el HMAC de `CanonicalManifest` permite autenticar el manifiesto con la clave externa. Una suma SHA sin clave no basta contra un administrador de base de datos. Las firmas históricas se comprueban en la pantalla de integridad; exportar por sí solo no declara íntegro todo el historial.
