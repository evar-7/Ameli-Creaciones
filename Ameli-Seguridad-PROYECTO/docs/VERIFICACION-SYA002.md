# Verificación de la actualización SYA-002

Se revisaron los tres escenarios en el código de la API y la web:

1. La recuperación busca el correo normalizado, responde públicamente con el mismo mensaje y encola el enlace solo para una cuenta habilitada. El destinatario es el correo almacenado en esa cuenta. El transporte `Smtp` envía por STARTTLS con el remitente y la credencial configurados fuera del ZIP.
2. `ValidateResetAsync` rechaza al abrir un enlace vencido o consumido. `ResetPasswordAsync` vuelve a comprobar el token dentro de una transacción y marca como usados los enlaces pendientes tras el cambio. La interfaz muestra **Solicitar otro enlace**.
3. La API devuelve una lista de reglas incumplidas para `Password` y `ConfirmPassword`. Un rechazo no modifica el hash de contraseña ni consume el enlace. La web indica las reglas conforme se escribe y conserva el formulario al recibir un error del servidor.

Se añadieron pruebas para el destinatario, el mensaje público, la reposición tras el vencimiento, la política por regla, la conservación del hash y los requisitos de configuración SMTP. **Estas pruebas no se ejecutaron en este entorno**: falta el SDK .NET, y las pruebas de integración necesitan además SQL Server. Ejecuta **Prueba → Explorador de pruebas → Ejecutar todas** en Visual Studio con LocalDB. Los resultados `.trx` antiguos dentro del ZIP corresponden a versiones anteriores.

`node --check` se ejecutó correctamente para `security.js`, `management.js` y `catalogs.js`. Una prueba aislada del JavaScript confirmó que marca reglas de contraseña, muestra errores de campo sin vaciar el formulario y ofrece pedir otro enlace si el servidor lo rechaza. Esta prueba no sustituye una revisión interactiva en el navegador. No se pudo verificar una entrega de correo real porque las credenciales y el proveedor del remitente deben configurarse en el equipo de destino. Sigue [SYA-002-RECUPERACION.md](../SYA-002-RECUPERACION.md) para esa prueba.
