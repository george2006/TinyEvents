# TinyEvents — trabajo actual

15 octubre 2026. **TE1c medido; Jorge descarta el índice para priorizar el procesamiento.**

Leer [OPERATIONS-STATUS-PLAN.md](OPERATIONS-STATUS-PLAN.md). TE1c aprobado. Siguiente slice: TE2a (PostgreSQL ADO.NET).
La propuesta añade un lector de estado para una BD/outbox; HTTP, tenants y auth pertenecen al host.
Plan guardado en main en `200399a`; trabajo en `feature/outbox-operational-status`.
TE1a añade contrato y resultado inmutable: dos archivos de producción y uno de tests.
Decisión de revisión: reader scoped registrado por cada provider en su inicialización habitual,
sin opt-in ni cast de ITinyOutboxStore. Esos registros se implementan en los slices de providers.
Verificación tras el ajuste: compilación Release y los 4 casos de TinyOutboxStatusTests pasan.
TE1a aprobado y commiteado en `b598465`.
TE1b aprobado y commiteado en `5fb2908`.
TE1b añade consulta de agregados PostgreSQL EF Core y registro directo scoped. Reutiliza conexión
y transacción del DbContext y captura una única hora de observación con TimeProvider.
Verificación TE1b: 9 casos nuevos pasan; suite completa del provider 76/76 con PostgreSQL real,
sin fallos ni omitidos. Compilación Release sin warnings. No hay migración ni cambio de índices;
TE1c: Docker recuperado y mediciones completadas en PostgreSQL 16 con un millón de filas.
[Evidencia reproducible](docs/performance/postgresql-outbox-status.md): 0,31 ms con historial
y poco trabajo pendiente; 30–32 ms con backlog de 300.000; 114 ms tras escrituras sin vacuum.
El candidato mejora unos 4–5 ms en lectura y ocupa 14 MB, pero añade 5–11% en inserción
masiva y 8–12% en actualización masiva sintética. Jorge descarta el índice: conservar schema 2,
sin migración 003. Se retiran los cambios experimentales de migración, modelos y tests.
Procesamiento concurrente sin fallos, con coste observable del sondeo; timeout verificado.
Solo documentación nueva en este slice, pendiente de commit.
Verificación final: solución Release sin warnings/errores y migrador PostgreSQL 8/8 + SQL Server
8/8 con BD real, sin omitidos. No quedan diferencias en src/tests respecto al commit TE1b.
No se ha publicado NuGet. Publicación posterior a implementación/revisión,
con instrucción expresa y actualización coordinada de paquetes en Feelings.

Al aprobar un slice, registrar aquí commit, evidencia y siguiente acción. No ejecutar todos los
slices de golpe; código legible, tests de comportamiento y sin abstracciones especulativas.
