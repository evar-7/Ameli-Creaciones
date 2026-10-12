# SYA-002 · Recuperación de contraseña con correo real

La página **https://localhost:7240/recuperar** recibe el correo registrado en Ameli. La respuesta pública es igual si existe o no existe la cuenta. Para una cuenta activa y habilitada, la API crea un enlace aleatorio de un solo uso que vence **15 minutos después de la solicitud** y lo envía a la dirección registrada. Una nueva solicitud invalida los enlaces anteriores. Un enlace vencido o consumido muestra **Solicitar otro enlace**.

La contraseña debe tener entre 8 y 64 caracteres, mayúscula, minúscula, número y símbolo. El formulario marca cada requisito y la API devuelve los incumplidos por campo. Un intento rechazado conserva la contraseña anterior y el enlace sigue vigente hasta que venza. Un cambio exitoso cierra las sesiones anteriores.

## Activar el envío SMTP en Visual Studio

1. Necesitas una cuenta de correo real que pueda enviar mediante **SMTP con STARTTLS**, más los datos del proveedor: servidor, puerto (normalmente 587), usuario y credencial SMTP o contraseña de aplicación. El remitente debe estar autorizado por ese proveedor. Si el proveedor solo ofrece OAuth, este adaptador SMTP de usuario/contraseña no le sirve.
2. En Visual Studio, clic derecho en **Ameli.Api → Administrar secretos de usuario**. **Conserva** la conexión SQL, las claves y las opciones que ya te funcionan. Añade estas entradas dentro del JSON existente, reemplazando los marcadores por tus datos reales:

   ```json
   "Email:Mode": "Smtp",
   "Email:From": "tu-remitente@correo.com",
   "Email:Host": "smtp.tu-proveedor.com",
   "Email:Port": 587,
   "Email:Username": "tu-remitente@correo.com",
   "Email:Password": "TU_CREDENCIAL_SMTP",
   "Security:WebBaseUrl": "https://localhost:7240"
   ```

   Si ya existen esas claves, cambia sus valores en vez de duplicarlas. Guarda con **Ctrl+S** y reinicia **Ameli.Api** y **Ameli.Web**. No guardes contraseñas SMTP en `appsettings.json` ni compartas `secrets.json` o los archivos de `App_Data`.
3. La cuenta cuya contraseña quieres recuperar debe tener un **correo real registrado en Ameli**. El formulario no permite redirigir el enlace de una cuenta a otro buzón. Si tu usuario de prueba termina en `@ameli.test`, inicia como administrador y corrige el correo de una cuenta interna en **Cuentas internas → Editar**, o usa desde el principio un correo real para el administrador inicial. Cambiar `Bootstrap:AdminEmail` después del primer arranque no modifica una cuenta ya creada.
4. Abre **https://localhost:7240/recuperar** en el mismo equipo, escribe ese correo registrado y solicita el enlace. Espera unos segundos y revisa entrada y spam. Abre el enlace **en el equipo donde está ejecutándose la web**: `localhost` de otro teléfono o computadora apuntaría a ese otro dispositivo.
5. Si el correo no aparece, mira la salida de **Ameli.Api** y las columnas `attempts`, `sent_at_utc` y `next_attempt_at_utc` de `email_outbox` en tu SQL Server. La respuesta pública seguirá siendo genérica y el servidor reintentará hasta cinco veces sin mostrar datos de la cuenta.

La configuración de desarrollo predeterminada continúa usando `Pickup` para escribir `.eml` localmente. Al poner `Email:Mode` en `Smtp` dentro de los secretos de **Ameli.Api**, se usa el transporte real. La API comprueba la configuración al arrancar y muestra qué ajuste falta si el SMTP está incompleto. La confirmación de solicitud significa que el mensaje se puso en cola; la entrega depende de que el proveedor acepte el envío. No se requiere cambiar la base de datos para esta actualización.

## Verificación sugerida

- Un correo registrado y uno inexistente muestran el mismo mensaje; solo el registrado recibe un enlace.
- Un enlace válido permite cambiar la contraseña una vez. Al abrirlo de nuevo, ofrece solicitar otro.
- Después de 15 minutos, el enlace se rechaza y se puede pedir otro.
- Una contraseña débil indica los requisitos incumplidos, conserva la contraseña anterior y permite corregirla con el mismo enlace vigente.
- Si solicitas un segundo enlace, usa el más reciente.
