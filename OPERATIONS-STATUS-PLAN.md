# TinyEvents — estado operativo de un outbox

15 octubre 2026 · **Arquitectura y slicing aceptados para empezar TE1a; no implementada.**
Base inspeccionada: `9c92375` (código de `1.0.0-beta.1`).
Consumidor inicial: [Feelings Operations](../Feelings/OPERATIONS-READINESS-PLAN.md).
Guías locales consultadas: TinyEvents-Coding-Guide.md y docs/way-of-working.md (no versionadas).
Se mantienen sus reglas de API aditiva, paridad, cambios pequeños y publicación solo autorizada.

## TE0 — decisión de arquitectura

TinyEvents procesa **una base de datos por scope**. Debe ofrecer una lectura pequeña de su
estado, no gestionar tenants ni descubrir workers.

**Cambio respecto al encargo inicial, discutido el 15 de octubre:** la API HTTP la publica
Feelings.Worker. TinyEvents aporta contrato y consulta; no añadimos ahora un paquete ASP.NET,
`MapTinyEventsOperations`, políticas, API keys ni health checks web.
La extracción futura de un mapper HTTP requiere un consumidor real adicional y aprobación.

Esto conserva la separación incluso con tres réplicas: todas pueden leer el mismo outbox.
La respuesta representa ese almacén, no el trabajo exclusivo de la réplica que responde.
No sumar snapshots de una misma base de datos. Un `WorkerId` identifica un propietario de claim;
no es la identidad estable de un servicio ni un registro de miembros del clúster.

### Responsabilidad exacta

- TinyEvents define qué significan pendiente, en proceso, fallido terminal y éxito retenido.
- Un proveedor ejecuta agregados sobre la tabla configurada, sin cargar mensajes.
- El host selecciona la conexión/scope y decide frecuencia, autenticación, HTTP, caché y UI.
- La consulta es exclusivamente de lectura; no reclama, renueva, limpia, reintenta ni reenvía.
- Instalar/actualizar paquetes no abre puertos ni registra endpoints.

## Lo que existe

| Componente | Evidencia y efecto |
| --- | --- |
| `src/TinyEvents/Outbox/TinyOutboxMessage.cs` | Id UUID, EventType, Payload, Status, AttemptCount, ClaimedBy, ClaimedAtUtc, ClaimExpiresAtUtc, CreatedAtUtc, NextAttemptAtUtc, ProcessedAtUtc, LastError |
| `TinyOutboxMessageStatus` | Pending=0, Processing=1, Processed=2, Failed=3 |
| `Processing/TinyOutboxProcessor.cs` | Reclama un lote, ejecuta secuencialmente; fallo recuperable vuelve a Pending con próxima fecha; al agotar intentos pasa a Failed |
| `Registry/TinyEventDispatcher.cs` | Invoca todos los consumidores del evento en secuencia; no persiste resultado por consumidor; un reintento repite la entrega del evento |
| Proveedores PostgreSQL EF/ADO | `FOR UPDATE SKIP LOCKED`; finalización condicionada por Id, ClaimedBy y Processing |
| Proveedores SQL Server EF/ADO | Implementaciones independientes del mismo contrato; no introducir una capacidad que silenciosamente falte en uno |
| `TinyEventsOptions` | Por defecto lote 50, 5 intentos, retry 30 s, claim 5 min; WorkerId generado único por proceso |
| `TinyEventsWorkerOptions` | Cleanup independiente; retención de procesados por defecto 1 hora, lotes 1.000, intervalo 1 s |
| `Processing/TinyOutboxProcessorLog`, `TinyEvents.Worker/*Log` | Logs estructurados de fallos/lease/recuperación; no registro durable de último éxito ni por consumidor |
| DI | `UseTinyEvents()`, `UsePostgreSqlEntityFrameworkCoreOutbox<T>()`, `AddTinyEventsWorker()` devuelven IServiceCollection; no builder AddTinyEvents().AddOperations() existente |
| HTTP | No paquete ni extensión ASP.NET existente; core y worker target net8.0 |

### Garantías que esta feature no amplía

La entrega sigue siendo **al menos una vez**. El lease no se renueva durante el consumidor;
puede expirar mientras un proceso continúa haciendo efectos externos. Condicionar el ACK evita
confirmar un claim ajeno, pero no deshace un email/pago. Los consumidores deben ser idempotentes.
Esta feature no añade exactly-once, fencing, leases de jobs ni coordinación de réplicas.

## Contrato propuesto

Una interfaz separada evita añadir miembros a `ITinyOutboxStore` y romper proveedores externos:

```csharp
public interface ITinyOutboxStatusReader
{
    ValueTask<TinyOutboxStatus> ReadAsync(CancellationToken cancellationToken = default);
}
```

`TinyOutboxStatus` es un resultado nombrado e inmutable. Sin tuplas. Campos propuestos:

| Campo | Significado exacto |
| --- | --- |
| `ObservedAtUtc` | Hora UTC capturada una vez para esta observación; no es un heartbeat |
| `PendingCount` (long) | Status Pending, incluyendo reintentos diferidos; excluye Processing y Failed |
| `ProcessingCount` (long) | Status Processing, lease vigente o vencido |
| `ExpiredProcessingCount` (long) | Subconjunto Processing con ClaimExpiresAtUtc <= ObservedAtUtc |
| `FailedCount` (long) | Status Failed: intentos agotados según el flujo actual, no el número histórico de excepciones |
| `OldestPendingAtUtc` (nullable) | Mínimo CreatedAtUtc entre Pending y Processing: trabajo todavía recuperable sin completar |
| `OldestPendingAge` (nullable TimeSpan) | max(0, ObservedAtUtc − OldestPendingAtUtc); incluye tiempo en retry y ejecución, excluye fallos terminales |
| `LastProcessedAtUtc` (nullable) | Máximo ProcessedAtUtc de filas Processed **todavía retenidas**; no un histórico durable |

La suma PendingCount + ProcessingCount es trabajo pendiente de completar. Documentar esa
distinción junto al nombre `OldestPendingAge`: no es solo el subconjunto Status Pending.
Sin trabajo pendiente, antigüedad null, no cero. Sin éxitos retenidos, último éxito null:
no significa «nunca se procesó nada» ni que el worker esté muerto.

Sin `ProcessedCount`: la limpieza lo haría decrecer sin significado de throughput.
Sin endpoint ni DTO de consumidores: la persistencia no permite atribuir backlog/fallo a un
consumidor concreto. No agrupar por EventType y llamarlo consumidor.
Sin payload, LastError, stack, connection string, usuario o nombre de base de datos.

## Consulta y coste

Índices actuales (schema inicial + migración 002):

- `(Status, NextAttemptAtUtc, CreatedAtUtc)` para pending.
- `(Status, ClaimExpiresAtUtc)` para Processing expirado.
- `(ClaimedBy, Status)` para propietario.
- `(Status, ProcessedAtUtc, Id)` para limpieza y último éxito retenido.

Propuesta: una sentencia por observación con agregados/subconsultas indexables sobre estados
concretos, usando la tabla validada del proveedor y parámetros; hora capturada una vez.
Contar estados en índices de Status; consultar expirados con el índice correspondiente;
buscar el último ProcessedAtUtc por el índice de limpieza. No hacer `SELECT *`, materializar
entidades ni contar todo el historial de procesados. Reutilizar gestión de conexión/transacción
EF y las factories ADO existentes; no cerrar una conexión propiedad del DbContext.

**Un COUNT exacto no es O(1).** Puede recorrer muchas entradas de índice con backlog grande;
no prometer «sin scans». En TE1c medir `EXPLAIN (ANALYZE, BUFFERS)` con datos sintéticos de
historial abundante y pendientes abundantes, sin bloquear procesamiento con locks explícitos.
En SQL Server revisar plan real/lecturas y aislamiento: no introducir NOLOCK para aparentar velocidad.
Consistencia de lectura concurrente según el aislamiento existente del proveedor; es una
observación operativa, nunca una barrera de sincronización ni prueba de drenaje.

Índice candidato, **solo si el plan lo justifica**: `(Status, CreatedAtUtc)` para el mínimo
de Pending/Processing. Añadir mediante migración 003 en ambos motores y catálogo existente;
no modificar 001/002 ni retirar índices actuales. Registrar coste de escritura y tamaño.
Si no hace falta, cerrar el subpaso con evidencia sin crear migración.

## Registro y uso

Propuesta opt-in explícita, respetando IServiceCollection:

```csharp
services.UsePostgreSqlEntityFrameworkCoreOutbox<ApplicationDbContext>();
services.AddTinyEventsOperations();
// Host resuelve ITinyOutboxStatusReader en el scope de la conexión apropiada.
```

Los stores existentes implementarán opcionalmente `ITinyOutboxStatusReader`, igual que hoy
implementan `ITinyOutboxCleanupStore`. `AddTinyEventsOperations` registra una factory scoped
que obtiene el ITinyOutboxStore ya elegido y comprueba esa capacidad. Sin descriptor nuevo,
reflection, segunda instancia del store ni I/O al registrar. El contrato de ITinyOutboxStore no cambia.
Proveedor externo sin soporte debe fallar explícitamente al resolver operaciones: no devolver
ceros, no impedirle seguir publicando/procesando cuando no ha habilitado esta capacidad.

La interfaz nueva tiene dos consumidores reales: monitor del worker y endpoint del host.
La responsabilidad del store sigue siendo persistencia del outbox; separar construcción de SQL
y lectura del resultado en métodos con una misión. No introducir una familia paralela de readers.

## Slicing

Estados: propuesto / implementado / revisado / aprobado / commit / verificado en consumidor.
No avanzar al siguiente commit sin revisión. Las rutas nuevas son propuestas, no archivos existentes.

### TE1a — contrato y opt-in, sin SQL

- **Objetivo:** acordar y compilar la superficie pública mínima, separada de cambios de provider.
- **Base:** ITinyOutboxStore/ITinyOutboxCleanupStore y IServiceCollection existentes.
- **Cambios:** `src/TinyEvents/Operations/{ITinyOutboxStatusReader,TinyOutboxStatus}.cs` y
  registro opt-in. Ningún miembro añadido a contratos antiguos ni provider modificado.
- **Pruebas:** incompatibilidad explícita del proveedor sin capacidad al resolver operaciones;
  consumo sin opt-in no altera publicación/procesamiento. No tests de DTO o wiring trivial.
- **No objetivos:** SQL, paquetes publicados, host HTTP o nuevas métricas históricas.
- **Decisión:** aprobar la API pública y semántica retenida antes de implementar.

### TE1b — PostgreSQL EF Core

- **Objetivo:** leer correctamente un outbox real, el proveedor usado por Feelings.
- **Base:** contrato TE1a, provider EF, tabla configurable y TimeProvider existentes.
- **Cambios:** `TinyPostgreSqlEfCoreOutboxStore` y `Sql/TinyPostgreSqlEfCoreSql`.
- **Pruebas:** BD vacía; cada estado; retries futuros; processing expirado; edad de creation y
  límites de reloj; éxito retenido/purgado; tabla/esquema personalizados; cancelación y error de BD;
  no mutaciones ni contenido en resultado. Proveedor externo sin capacidad falla al optar.
- **No objetivos:** otros proveedores, HTTP, métricas históricas, cambios al procesador.
- **Decisiones:** forma SQL/aislamiento explícitos; no reabrir contrato sin evidencia.

### TE1c — coste e índice si procede

- **Objetivo:** demostrar que la consulta no perjudica materialmente al consumidor.
- **Base:** TE1b, índices y migrador versionado.
- **Cambios:** planes SQL documentados; migración 003 y catálogo únicamente si se justifica.
- **Pruebas:** alta proporción de Processed y backlog grande, procesamiento simultáneo, timeout;
  si hay migración, instalación nueva/upgrade/repetición y tabla personalizada.
- **No objetivos:** benchmarks genéricos, caché en librería, índice por consumidor.
- **Decisión:** aceptar medición o aprobar índice con coste explícito.

### TE2a / TE2b / TE2c — paridad de proveedores, tres commits

- **Objetivo:** misma semántica en PostgreSQL ADO, SQL Server ADO y SQL Server EF, respectivamente.
- **Base:** readers/factories/SQL/table names de cada proveedor, contrato TE1.
- **Cambios:** store y SQL de cada proyecto; migración de índice si fue aprobada.
- **Pruebas:** misma matriz de comportamiento contra cada motor, tabla no predeterminada,
  transacción EF existente y ausencia de cambios a claims/cleanup.
- **No objetivos:** consolidar todo el SQL de cuatro paquetes como refactor colateral.
- **Decisión:** paridad antes de publicar una versión que anuncie soporte general.

### TE3 — documentación y paquete

- **Objetivo:** capacidad pública pequeña, documentada y consumible desde un paquete real.
- **Base:** `docs/schema-and-migrations.md`, `docs/workers.md`, `docs/retention-and-cleanup.md`,
  sample PackageSmoke y pipeline de release existente.
- **Cambios:** `docs/operations.md`, índice docs/README y release notes; ejemplo simple sin HTTP.
  Actualizar gates de API pública/paquetes existentes, no inventar suite de wiring.
- **Pruebas:** pack/restore en consumidor aislado; opt-in ausente conserva comportamiento;
  cuatro proveedores soportados; serializer del contrato no expone contenido.
- Antes de una release candidata: checkout limpio, build/tests de ambos motores habilitados,
  inspección de todos los paquetes y API pública, smoke consumidor y revisión humana. No
  declarar `1.0.0` listo por terminar estos slices; respetar docs/way-of-working.md.
- **No objetivos:** publicar NuGet sin instrucción, asignar nueva versión sin acuerdo.
- **Decisión:** versión coordinada de paquetes y política de compatibilidad.

### TE4 — entrega a Feelings

- **Objetivo:** Feelings usa el paquete publicado, sin copia de SQL o ProjectReference local.
- **Base:** matriz de paquetes beta.1 de ambos repositorios y el plan O2/O3 de Feelings.
- **Cambios:** referencias en Feelings al mismo tren de versión; retirar SQL propio del monitor
  únicamente en el slice O3, con adaptación de alertas.
- **Pruebas:** monitor/master/tenant con paquete real y no filtración entre scopes.
- **No objetivos:** relanzar toda la suite de Feelings por cada commit de TinyEvents.
- **Decisión:** release aprobada y contrato cerrado antes del consumidor.

## Aprobación y cierre

TE0 aceptado para empezar el 15 de octubre: reader sin HTTP, una BD por scope, registro opt-in,
paridad de cuatro proveedores y sin métricas por consumidor. Jorge pide trabajar como autores
OSS, guardar el plan en main y crear rama. Revisar el contrato público concreto en TE1a;
esta aprobación no permite saltarse las revisiones de slices ni publicar NuGet.

Hecho cuando los tests de comportamiento pasan, el paquete se consume fuera del repo,
Feelings no reproduce consultas de TinyOutbox y la documentación no promete historial ni
exclusión de procesos que la librería no proporciona. Auth pertenece completamente al host.

| Slice | Estado | Commit / evidencia |
| --- | --- | --- |
| TE0 | Aceptado para empezar | Instrucción de Jorge, 15 octubre 2026 |
| TE1a / TE1b / TE1c | No iniciado | — |
| TE2a / TE2b / TE2c | No iniciado | — |
| TE3 / TE4 | No iniciado | — |
