# TinyEvents — trabajo actual

15 octubre 2026. **Arquitectura y slicing aceptados; empezar TE1a.**

Leer [OPERATIONS-STATUS-PLAN.md](OPERATIONS-STATUS-PLAN.md). Siguiente paso: TE1a, contrato y opt-in sin SQL.
La propuesta añade un lector de estado para una BD/outbox; HTTP, tenants y auth pertenecen al host.
Jorge autoriza guardar este plan en main y crear una rama de trabajo. Aún no hay código nuevo
ni NuGet publicado. Publicación posterior a implementación/revisión,
con instrucción expresa y actualización coordinada de paquetes en Feelings.

Al aprobar un slice, registrar aquí commit, evidencia y siguiente acción. No ejecutar todos los
slices de golpe; código legible, tests de comportamiento y sin abstracciones especulativas.
