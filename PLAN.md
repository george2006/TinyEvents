# TinyEvents — trabajo actual

15 octubre 2026. **TE2c aprobado por Jorge; siguiente TE3.**

Leer [OPERATIONS-STATUS-PLAN.md](OPERATIONS-STATUS-PLAN.md). TE1c commiteado en `ebdb411`.
TE2a (PostgreSQL ADO.NET) commiteado en `1787ca2`.
TE2b (SQL Server ADO.NET) commiteado en `de7e227`.
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
Solo documentación nueva en TE1c, commiteada en `ebdb411`.
Verificación final: solución Release sin warnings/errores y migrador PostgreSQL 8/8 + SQL Server
8/8 con BD real, sin omitidos. TE1c no modifica src/tests respecto al commit TE1b.
TE2a implementa el reader en PostgreSQL ADO.NET: misma consulta, TimeProvider y registro
scoped; conexión propia por observación, sin participar en la transacción del publisher.
10/10 tests nuevos y suite completa PostgreSQL ADO.NET 150/150, sin omitidos, en Release.
Revisión: SQL parametrizado, sin cambios de estado, recursos liberados tras lectura/error,
cancelación propagada y constructor interno actualizado en tests existentes.
Seguimiento fuera de este slice: revisar disposal en la factory ADO si OpenAsync falla antes
de devolver la conexión. No se modifica esa factory ni se afirma cobertura de ese fallo.
TE2b implementa el mismo reader en SQL Server ADO.NET con COUNT_BIG, tabla validada,
parámetros y una conexión propia por observación. No cambia el aislamiento ni usa NOLOCK;
la observación concurrente depende del aislamiento configurado, no es un snapshot garantizado.
Jorge aprueba el slice durante la validación. 10/10 nuevos y suite SQL Server ADO.NET 165/165,
sin omitidos. Build Release sin warnings ni errores. Revisión de paridad, SQL y recursos cerrada.
Docker: el endpoint genérico falló; validación completada usando DOCKER_HOST de proceso
`npipe://./pipe/dockerDesktopLinuxEngine`, sin cambiar configuración del repositorio.
TE2c añade reader scoped en SQL Server EF Core, con la misma consulta COUNT_BIG de ADO.NET.
Reutiliza la conexión/transacción del DbContext sin cerrarlas, no añade tracking ni guarda cambios.
9/9 tests nuevos y suite SQL Server EF Core 46/46 con BD real, sin omitidos; build Release
sin warnings/errores. Revisión de SQL, recursos y paridad completada, sin hallazgos nuevos.
Aprobado por Jorge y cerrado en el commit de TE2c. Siguiente: TE3 (documentación y validación de paquetes).
Objetivo acordado: nueva beta con la capacidad operativa; benchmark AWS aplazado. Completar
paridad, validación y comprobación de paquetes antes de publicar; sin promesas de rendimiento cloud.
No se ha publicado NuGet. Publicación posterior a implementación/revisión,
con instrucción expresa y actualización coordinada de paquetes en Feelings.

Al aprobar un slice, registrar aquí commit, evidencia y siguiente acción. No ejecutar todos los
slices de golpe; código legible, tests de comportamiento y sin abstracciones especulativas.
