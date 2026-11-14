# Procedimiento de piloto controlado (no ejecutado)

Estado: procedimiento preparado; no se ha realizado un piloto ni se habilita producción. Requiere aprobación previa de Dueña/contador y cierre de F00-T02, F10, F08 y validaciones físicas pertinentes.

## Preparación y límites

1. Crear un usuario/perfil Windows o VM de ensayo aislado y una DB ficticia temporal. Registrar hashes/rutas. No copiar una DB productiva/histórica a pruebas sin autorización y protección adecuada.
2. Instalar solo el paquete aprobado para arquitectura/Windows comprobados. Validar que `Data`, `Backups`, `Secrets` y `Updates` están fuera de la instalación y que el programa no ejecuta en modo prototipo sin argumento explícito.
3. Usar solo nombres/productos/clientes/pagos ficticios, medios de prueba y Microsoft Print to PDF o impresora de prueba aprobada. No generar comprobantes fiscales ni cobros reales.
4. Preparar un respaldo verificable y una copia recuperable antes de cualquier actualización/restauración; dejar caja cerrada.

## Flujo de ensayo

1. Primer inicio: bootstrap de Dueña, crear empleado de prueba y verificar permisos; cerrar/abrir sesión.
2. Crear proveedor/producto/variantes, fijar precios ficticios, cargar recepción y consultar Disponible/Físico/Apartado.
3. Imprimir vista previa de etiquetas F11; revisar Código 128 y código interno legible. Simular escaneo con teclado; confirmar que el POS identifica variante exacta y valida precio/Disponible.
4. Abrir caja ficticia; venta con pago simple y combinado, vuelto, venta fiada autorizada, abono y consulta de deuda. Revisar cada histórico; no repetir confirmación si el resultado es incierto.
5. Probar cambio/devolución con liquidación de prueba y revisar entradas/salidas, stock apto/no apto, caja y ticket interno.
6. Ejecutar conteo físico controlado; revisar diferencias y aprobación por rol autorizado.
7. Crear y verificar backup local. Cuando OAuth/Drive esté autorizado, probar copia y restauración en otra DB/perfil aislado con código de recuperación protegido; no hacer restauración sobre la base de origen.
8. Simular cierre normal/reinicio. Verificar persistencia de venta, caja, cliente, cambios, etiquetas (sin modificar stock) y estado de actualización lista.
9. Con un release de ensayo firmado: buscar actualización, permitir descarga y verificación, cancelar instalación y confirmar caja abierta bloquea; cerrar caja y confirmar prompt. No enviar trabajos de impresión reales ni lanzar instalador en perfil personal.
10. Probar desinstalación solo en la VM/perfil desechable y confirmar que los datos del usuario se conservan; reinstalar/upgrade sobre otra copia y verificar migración/backups.

## Criterio y registro

La Dueña revisa saldos, stock e históricos; contador revisa procedimiento fiscal antes de uso comercial. Registrar errores, versiones, tiempos medidos, configuración Windows/hardware y decisión de continuar/detener. Un error monetario, de stock, duplicación, historial, backup o restore detiene el piloto y requiere diagnóstico antes de reintentar.

Fecha/responsable: ________  Build firmada: ________  PC/Windows (según checklist): ________  Casos ejecutados: ________  Incidentes: ________  Aprobación Dueña: ________  Revisión contador: ________

No marcar este documento como evidencia hasta ejecutar cada paso y guardar resultados reales.
