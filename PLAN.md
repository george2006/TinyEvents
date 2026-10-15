# TinyEvents — trabajo actual

15 octubre 2026. **TE1a aprobado por Jorge; siguiente slice TE1b.**

Leer [OPERATIONS-STATUS-PLAN.md](OPERATIONS-STATUS-PLAN.md). Siguiente: TE1b (PostgreSQL EF Core).
La propuesta añade un lector de estado para una BD/outbox; HTTP, tenants y auth pertenecen al host.
Plan guardado en main en `200399a`; trabajo en `feature/outbox-operational-status`.
TE1a añade contrato y resultado inmutable: dos archivos de producción y uno de tests.
Decisión de revisión: reader scoped registrado por cada provider en su inicialización habitual,
sin opt-in ni cast de ITinyOutboxStore. Esos registros se implementan en los slices de providers.
Verificación tras el ajuste: compilación Release y los 4 casos de TinyOutboxStatusTests pasan.
No se ha implementado SQL ni publicado NuGet. Publicación posterior a implementación/revisión,
con instrucción expresa y actualización coordinada de paquetes en Feelings.

Al aprobar un slice, registrar aquí commit, evidencia y siguiente acción. No ejecutar todos los
slices de golpe; código legible, tests de comportamiento y sin abstracciones especulativas.
