# Guía breve de Git Flow y cierre de tareas

## Flujo obligatorio por historia de usuario

1. Crear una rama por historia, ejemplo:
   - `feature/GID-xxx-descripcion-corta`
   - `feature/AAC-xxx-descripcion-corta`
   - `feature/IAR-xxx-descripcion-corta`
2. Implementar y validar localmente:
   - Build exitoso
   - Pruebas relevantes exitosas
3. Abrir Pull Request hacia `main`:
   - Referenciar historia/issue
   - Incluir evidencia breve (capturas/log de pruebas)
4. Revisión obligatoria:
   - Al menos 1 aprobación de otro integrante
   - Resolver observaciones antes de merge
5. Merge a `main` solo vía PR aprobado.

## Definition of Done (DoD) mínimo

- [ ] Código compilando sin errores.
- [ ] Pruebas relevantes en verde.
- [ ] Documentación actualizada (si aplica).
- [ ] PR aprobado por otro integrante.
- [ ] Tarjeta del tablero movida a **Done**.
- [ ] Issue relacionado cerrado.

## Nota operativa para tablero e issues

No se gestiona desde este repositorio directamente. Tras fusionar cada PR:

- Mover tarjetas de historias de usuario completadas a columna **Done**.
- Cerrar los issues vinculados indicando número de PR y commit de cierre.
