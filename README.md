# Retrospectiva y Bitácora del Sprint 3

## 1. Bitácora de actividades por integrante

La asignación oficial de tareas quedó así:

### Desarrollador 1 (evar-7)
- Corrección del feedback anterior.
- SYA-007 (verificación de respaldos diarios).
- IAR-006 (evaluación de rendimiento del motor de IA).
- GOL-001 (consulta y filtro de catálogo para visitantes).

### Desarrollador 2 (flujo de carrito y compras iniciales)
- GOL-002, GOL-003.
- GOL-004, GOL-005.
- GOL-011, GOL-012.

### Desarrollador 3 (logística y gestión de categorías)
- GOL-006, GOL-007, GOL-008.
- GOL-015, GOL-016.

## 2. Bitácora de actividades ejecutadas

### Desarrollador 1 (evar-7) — Correcciones del sprint anterior, SYA-007, IAR-006, GOL-001
- Aplicación de correcciones del sprint anterior con enfoque en estabilidad técnica.
- Ajuste del contrato de acceso denegado para evitar error 500 por metadata de validación en .NET 10.
- Limpieza de archivos de Visual Studio versionados por error (`.vs/`) del índice de Git.
- Implementación de **SYA-007** (verificación de respaldos diarios) con consulta prioritaria en SQL Server, fallback a carpeta local y registro en auditoría.
- Implementación de **IAR-006** mediante suite de evaluación de rendimiento del motor de recomendaciones (medición de latencia y memoria).
- En IAR-006 se validó la lógica de evaluación; no se incorporó UI dedicada en esta fase.
- Implementación de **GOL-001** para consulta y filtro de catálogo para visitantes (acceso anónimo y filtros de búsqueda/especie/stock).
- Corrección del bug en el benchmark de rendimiento del motor de recomendaciones que impedía ejecutar el proyecto.
- Implementación de la regla **Protect Main** para evitar push directo a `main` y ajuste del flujo de ramas para que cada historia tenga su propia rama de desarrollo.

### Desarrollador 2 (Brightax06) — carrito y compras
- Implementación de **GOL-002 y GOL-003**: agregar, modificar y eliminar artículos del carrito con validaciones de inventario.
- Implementación de **GOL-004 y GOL-005**: confirmación de compra y visualización de seguimiento/historial de pedidos del cliente.
- Implementación de **GOL-011 y GOL-012**: edición y desactivación de productos con validaciones de negocio y trazabilidad.
- Ajustes de pruebas y flujos de UI/API para consistencia de carrito, checkout y pedidos.

### Desarrollador 3 (Mizifuz) — logística y categorías
- Implementación de **GOL-006, GOL-007 y GOL-008**: flujo logístico de pedidos (preparación, despacho y entrega) con actualización de estados.
- Implementación de **GOL-015 y GOL-016**: edición y desactivación de categorías para administración del catálogo.
- Ajustes de reglas de transición de estados y controles de operación para roles administrativos/logística.
- Integración de cambios con rutas de gestión y pruebas funcionales del módulo logístico/catálogos.

## 3. Evaluación de participación del equipo

- **Participación técnica general:** Alta, con cobertura de seguridad, IA, catálogo, compras y logística.
- **Balance de carga:** Se distribuyeron US tomando en cuenta la carga y concordancia de cada historia, con bloques funcionales claros por desarrollador.
- **Cumplimiento de proceso:** En mejora; se corrigen desvíos de control de versiones detectados al cierre previo.

## 4. Conclusiones de la retrospectiva

1. **Fortalezas**
   - Buena separación de responsabilidades por dominio funcional.
   - Capacidad de respuesta rápida ante feedback técnico oficial.

2. **Oportunidades de mejora**
   - Mantener de forma estricta rama por historia y PR con revisión obligatoria.
   - Consolidar revisión pre-commit para evitar subir artefactos locales.

3. **Acciones concretas**
   - Continuar usando flujo por ramas (`feature/<historia>`).
   - Verificar `git status` y `.gitignore` antes de cada commit.
   - Mantener bitácora de actividades por integrante al cierre de cada sprint.

## 5. Nota de tablero

La actualización de tarjetas en GitHub Projects (movimiento a **Done**) y cierre de issues relacionados ya fue realizada manualmente por el equipo.
