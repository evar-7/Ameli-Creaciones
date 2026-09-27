# Cambios: contraseña inicial desde administración

- La pantalla **Administración > Cuentas internas > Nueva cuenta** ahora solicita `Contraseña inicial` y `Confirmar contraseña`.
- La API recibe esos campos mediante `CreateInternalAccountRequest` y valida la misma política usada por cambio/restablecimiento: 8–64 caracteres, mayúscula, minúscula, número y símbolo.
- La contraseña nunca se guarda en texto plano. `PasswordHasher<AppUser>` genera `PasswordHash` antes de persistir la cuenta.
- Ya no se crea automáticamente un token de restablecimiento al dar de alta una cuenta. Si la cuenta está activa, se envía únicamente un aviso de creación; la contraseña asignada no se manda por correo.
- En desarrollo, `Email:Mode=Pickup` escribe `.eml` en UTF-8 con `Content-Transfer-Encoding: 8bit`, para que el cuerpo sea legible directamente y no aparezca convertido a Base64.
- La recuperación de contraseña sigue funcionando con enlaces de un solo uso y tokens aleatorios.

No hay cambios de esquema de base de datos ni migraciones nuevas.


## Edición de cuentas
- La pantalla **Editar cuenta interna** también permite que un administrador asigne una nueva contraseña.
- Los campos son opcionales durante la edición: si ambos quedan vacíos, la contraseña actual se conserva.
- Si se cambia la contraseña, se valida la misma política de seguridad, se almacena únicamente el hash, se invalidan enlaces de recuperación pendientes y se cierran las sesiones de esa cuenta.
- El valor de la contraseña nunca se incluye en la auditoría.
