# Verificación de entrega

Fecha: 22 de septiembre de 2026.

## Compilación y base de datos

| Comprobación | Resultado |
| --- | --- |
| Compilación de solución y proyectos finales | Correcta, 0 errores y 0 advertencias. |
| Entorno de compilación | SDK .NET 10.0.401, runtime 10.0.12, Linux x64. |
| Motor utilizado | Microsoft SQL Server 2022 Developer, 16.0.4205.1. |
| Migración EF Core | Aplicada sobre una base de pruebas creada para la ejecución. |
| Script SQL | Generado como script idempotente desde `InitialSecurity`. |
| Pruebas automatizadas | **29 ejecutadas, 29 aprobadas, 0 fallidas y 0 omitidas**. |

Archivo de resultados de VSTest: [resultados-seguridad.trx](resultados-seguridad.trx). Las pruebas están incluidas en la solución y utilizan SQL Server real, incluyendo `sp_getapplock`; no se sustituyó la persistencia por una base en memoria.

Se comprobaron login por rol, rechazo genérico, validación de entradas, bloqueo con cinco solicitudes concurrentes, desbloqueo automático/administrativo, límites de recuperación, enlace expirado y consumo único concurrente, rechazo de contraseñas débiles, aislamiento de permisos, conservación del último administrador aun con cambios concurrentes, revocación por cambios de acceso, rotación del refresh y rechazo de JWT vencidos, alterados o pertenecientes a sesiones inactivas.

## Arranque automático de desarrollo

Esta actualización añade cinco pruebas: generación y conservación de la clave local, exclusión de esa clave en producción, prioridad de una clave configurada, inicialización concurrente con reinicio sin contraseñas Bootstrap, y reversión completa si una contraseña de prueba es inválida.

Además, se inició el ejecutable real de la API en `Development`, con SQL Server 2022 y HTTPS con certificado validado por el cliente, sin `--initialize` y sin configurar `Jwt:SigningKey`. Se verificó que preparara el esquema automáticamente y permitiera ingresar a las cuatro cuentas. Se reinició la API quitando ambas contraseñas Bootstrap y se comprobó que conservara la clave JWT, la sesión previa y la contraseña del administrador.

Esta comprobación valida el arranque de la aplicación; no sustituye la comprobación de la interfaz de Visual Studio en Windows.

## Flujos web por HTTPS verificados en la entrega anterior

Las vistas y los flujos web no cambian en esta actualización. En la entrega anterior se iniciaron SQL Server, la API y la web, y se realizaron solicitudes reales a la web con certificado de prueba validado por el cliente HTTP:

- Login sin antiforgery: rechazado con 400.
- Login de Administrador, Logística y Cliente: redirección y página correspondientes.
- Página «Mi seguridad» y lista de sesiones propias: accesibles con sesión.
- Navegación a una página autorizada: renueva actividad; consultas pasivas de sesión: conservan el plazo.
- Página administrativa: accesible al Administrador; Cliente y Logística redirigidos a «Sin acceso» y sin la opción administrativa en el menú.
- Logout: redirección a ingreso y revocación.
- Recuperación: solicitud desde formulario, generación de `.eml`, apertura de enlace, validación y restablecimiento exitoso.

En esa prueba el enlace interno web→API se ejecutó por HTTP de loopback en el ambiente `Testing`; la configuración entregada para desarrollo usa HTTPS en ambos proyectos. No se enviaron correos reales.

## Comprobaciones que requieren el equipo de destino

La ejecución dentro de Visual Studio / IIS en Windows y la conexión al proveedor SMTP real no se realizaron en este entorno Linux. Sigue el README para configurarlas. La revisión visual interactiva del navegador quedó limitada porque el navegador disponible bloqueó archivos locales; las páginas sí se renderizaron en servidor y se verificaron sus flujos HTTP. No se afirma compatibilidad visual probada en todos los navegadores o tamaños de pantalla.

Para la aceptación en Windows, inicia ambos proyectos y comprueba las páginas en escritorio y móvil; usa el segundo administrador de prueba para cambiar roles, verifica los correos locales, deja una sesión 15 minutos sin tocarla y confirma la redirección por inactividad. Los límites temporales y de concurrencia ya tienen pruebas automatizadas con reloj controlado.
