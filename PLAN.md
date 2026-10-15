# TinyEvents — trabajo actual

15 octubre 2026. **TE1b aprobado por Jorge; siguiente slice TE1c.**

Leer [OPERATIONS-STATUS-PLAN.md](OPERATIONS-STATUS-PLAN.md). Siguiente: TE1c (medición de índices).
La propuesta añade un lector de estado para una BD/outbox; HTTP, tenants y auth pertenecen al host.
Plan guardado en main en `200399a`; trabajo en `feature/outbox-operational-status`.
TE1a añade contrato y resultado inmutable: dos archivos de producción y uno de tests.
Decisión de revisión: reader scoped registrado por cada provider en su inicialización habitual,
sin opt-in ni cast de ITinyOutboxStore. Esos registros se implementan en los slices de providers.
Verificación tras el ajuste: compilación Release y los 4 casos de TinyOutboxStatusTests pasan.
TE1a aprobado y commiteado en `b598465`.
TE1b añade consulta de agregados PostgreSQL EF Core y registro directo scoped. Reutiliza conexión
y transacción del DbContext y captura una única hora de observación con TimeProvider.
Verificación TE1b: 9 casos nuevos pasan; suite completa del provider 76/76 con PostgreSQL real,
sin fallos ni omitidos. Compilación Release sin warnings. No hay migración ni cambio de índices;
TE1c medirá el coste antes de decidir si hace falta migración 003.
No se ha publicado NuGet. Publicación posterior a implementación/revisión,
con instrucción expresa y actualización coordinada de paquetes en Feelings.

Al aprobar un slice, registrar aquí commit, evidencia y siguiente acción. No ejecutar todos los
slices de golpe; código legible, tests de comportamiento y sin abstracciones especulativas.
