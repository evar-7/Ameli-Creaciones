# Abrir y ejecutar desde Visual Studio

Esta versión se configura con las ventanas de Visual Studio. No necesitas PowerShell, CMD ni ejecutar archivos `.ps1`.

## 1. Abrir la solución

Descomprime el ZIP en una carpeta y abre **Ameli.Seguridad.sln** con Visual Studio 2026 (18.0 o posterior). Espera a que termine la restauración de paquetes.

Necesitas la carga «Desarrollo de ASP.NET y web», el SDK .NET 10 y SQL Server LocalDB. Si falta LocalDB, abre **Visual Studio Installer → Modificar → Componentes individuales**, busca **SQL Server Express LocalDB**, selecciónalo y aplica los cambios. SQL Server Management Studio por sí solo no instala el motor.

## 2. Elegir las credenciales de desarrollo

En el **Explorador de soluciones**, clic derecho en el proyecto **Ameli.Api → Administrar secretos de usuario** (*Manage User Secrets*). Se abre `secrets.json`, fuera del proyecto.

Si está vacío, pega esto:

```json
{
  "Bootstrap:AdminEmail": "admin@ameli.test",
  "Bootstrap:AdminPassword": "REEMPLAZAR_POR_TU_CLAVE",
  "Bootstrap:CreateDemoUsers": true,
  "Bootstrap:DemoPassword": "REEMPLAZAR_POR_TU_CLAVE_DE_PRUEBA"
}
```

Reemplaza los dos marcadores de contraseña por claves elegidas por ti. Cada una debe tener de 8 a 64 caracteres e incluir mayúscula, minúscula, número y símbolo. Puedes cambiar el correo por otro válido. Guarda con **Ctrl+S**. No pegues las contraseñas en el chat ni las guardes en `appsettings.json`.

Si `secrets.json` ya tiene una conexión SQL u otros valores configurados, conserva esas entradas y añade las que falten dentro del mismo objeto JSON.

La aplicación usa `(localdb)\MSSQLLocalDB` y la base `AmeliSecurity` de forma predeterminada. Si utilizas una instancia diferente de SQL Server, añade `ConnectionStrings:DefaultConnection` a los secretos con la conexión correspondiente. Por ejemplo, si tu instancia se llama SQLEXPRESS:

```json
"ConnectionStrings:DefaultConnection": "Server=.\\SQLEXPRESS;Database=AmeliSecurity;Trusted_Connection=True;TrustServerCertificate=True"
```

La clave JWT se genera automáticamente la primera vez en modo Development y se conserva para los siguientes arranques. No debes escribirla a mano.

## 3. Seleccionar los dos proyectos de inicio

Selecciona **Ameli - API y Web** en el selector junto al botón verde de ejecutar si Visual Studio muestra el perfil compartido.

Si no aparece, clic derecho en la **solución Ameli.Seguridad**, arriba de los proyectos → **Configurar proyectos de inicio** (o **Propiedades → Proyecto de inicio**) → **Varios proyectos de inicio**:

| Proyecto | Acción |
| --- | --- |
| Ameli.Api | Iniciar |
| Ameli.Web | Iniciar |
| Ameli.Contracts | Ninguna |
| Ameli.Security.Tests | Ninguna |

Deja la API primero. Selecciona el perfil **https** de los dos proyectos. Guarda con **Aceptar**.

## 4. Presionar F5

Visual Studio compila e inicia la API y la web. Si solicita confiar en el **certificado HTTPS de desarrollo de ASP.NET Core**, acepta ese certificado local.

En el primer arranque, la API crea/aplica las tablas en SQL Server y crea las cuentas elegidas. Espera a que muestre «Base de datos de desarrollo preparada». La web se abre en **https://localhost:7240/ingresar**.

| Correo | Contraseña |
| --- | --- |
| `admin@ameli.test` o el correo que elegiste | `Bootstrap:AdminPassword` que escribiste |
| `admin2@ameli.test` | `Bootstrap:DemoPassword` que escribiste |
| `logistica@ameli.test` | `Bootstrap:DemoPassword` que escribiste |
| `cliente@ameli.test` | `Bootstrap:DemoPassword` que escribiste |

Para detener, usa **Shift+F5**. Los siguientes arranques conservan las cuentas, roles y contraseñas. Cambiar las claves de Bootstrap no restablece contraseñas existentes; utiliza la recuperación de contraseña para eso.

Después de la primera inicialización correcta puedes eliminar las entradas `Bootstrap:AdminPassword` y `Bootstrap:DemoPassword` de los secretos: las cuentas existentes no las requieren para arrancar de nuevo.

## Correos para probar recuperación

Se guardan como `.eml` en **src/Ameli.Api/App_Data/mail**, aproximadamente dentro de cinco segundos. Abre el mensaje con tu cliente de correo o con un editor para seguir el enlace. Para enviar correos reales, configura SMTP según `docs/CONFIGURACION.md`.

## Si aparece un error

- **Falta el SDK:** instala .NET 10 con Visual Studio Installer.
- **No encuentra SQL Server:** comprueba LocalDB o configura el nombre de tu instancia; SSMS es una herramienta de administración y requiere un motor aparte.
- **Faltan datos Bootstrap:** verifica que editaste los secretos de **Ameli.Api**, reemplazaste los marcadores y guardaste el archivo.
- **Servicio no disponible al ingresar:** espera a que la API termine de iniciar y comprueba que ambos proyectos estén ejecutándose.
- **Certificado no confiable:** revisa y acepta el certificado local de desarrollo desde el aviso de Visual Studio.

El arranque automático de base y cuentas se habilita solo en **Development**. Producción conserva la configuración explícita de SQL, JWT, correo y migraciones descrita en la documentación técnica.

Referencias oficiales: [Secretos de usuario en Visual Studio](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0), [Varios proyectos de inicio](https://learn.microsoft.com/en-us/visualstudio/ide/how-to-set-multiple-startup-projects?view=visualstudio).
