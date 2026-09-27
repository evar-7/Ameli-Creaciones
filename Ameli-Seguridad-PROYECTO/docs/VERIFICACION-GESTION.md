# Verificación — gestión interna y auditoría

## Resultado comprobado en esta entrega

- Solución .NET 10: compilación sin errores ni advertencias.
- 18 pruebas sin SQL ejecutadas: firmas ante alteraciones, cabecera, claves, validaciones de datos y persistencia de claves de desarrollo. Evidencia: `resultados-sin-sql.trx`.
- JavaScript nuevo: sintaxis comprobada con `node --check`.
- Migración EF y script SQL idempotente generados; preservan datos y agregan revisiones de edición individuales.

## Pendiente de comprobar con SQL Server

El motor SQL Server del entorno de trabajo falló al arrancar antes de cargar el proyecto. Por eso no se presentan como aprobadas las pruebas de integración nuevas ni la suite completa anterior sobre esta versión. Tampoco se ha ejecutado Visual Studio/SQL Express en Windows ni una prueba interactiva completa del navegador.

Se incluyen `InternalAuditTests.cs`, la suite de seguridad original y una matriz de 24 escenarios. Prueban CRUD, privacidad, concurrencia, bloqueo, revocación, filtros, exportación, alteraciones/borrados de auditoría y migración con datos existentes.

En Visual Studio: **Prueba → Explorador de pruebas → Ejecutar todas**. Las pruebas SQL usan LocalDB de forma predeterminada. Para otra instancia, configura `AMELI_TEST_SQL` mediante las variables de entorno de Windows con una conexión de pruebas que pueda crear/eliminar bases. Cada ejecución utiliza `AmeliSecurityTests_<guid>` y la elimina al finalizar; no modifica `AmeliSecurity`.

Prueba manual después de iniciar API y Web: crear una cuenta, filtrar y limpiar, probar correo repetido/teléfono inválido, editar desde dos pestañas, cancelar y confirmar un cambio de estado, intentar desactivar el último administrador, ingresar como Logística, consultar auditoría, exportar y verificar integridad.

Los archivos `VERIFICACION.md` y `resultados-seguridad.trx` corresponden a la entrega anterior, no certifican esta ampliación.
