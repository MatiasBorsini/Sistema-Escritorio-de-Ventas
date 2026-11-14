# Guía breve de uso — LocalInfantil

Esta guía describe los flujos implementados, no habilita el uso real. La validación de Windows/PC del local, fiscalidad, backups/Drive y permisos de Dueña/contador está pendiente. Para practicar, usar exclusivamente una base temporal con `--dev-db`.

## Acceso y tareas habituales

1. Abrir LocalInfantil e ingresar con el usuario entregado por la Dueña. En una base nueva aparece el bootstrap inicial de Dueña; la primera clave debe ser personal y no compartirse.
2. Abrir caja al comenzar el turno y verificar saldo inicial. La caja debe cerrarse desde el flujo normal; no se instala una actualización ni se restaura una base con caja abierta.
3. En POS, buscar por nombre/código o ingresar el código interno de una etiqueta F11. Revisar producto, variante, precio vigente y cantidad antes de confirmar.
4. Elegir medio/s de pago. El vuelto es informativo, no otro medio. Para fiado se requiere cliente activo, límite y permiso; confirmar importe recibido/deuda antes de aceptar.
5. Consultar historial de ventas/caja desde las pantallas habilitadas. Cambios y devoluciones usan su propio flujo y ticket interno; no son comprobantes fiscales.
6. Consultar stock por variante: Físico, Disponible y Apartado. Recepción registra mercadería; conteo compara cantidades reales; apartar/reincorporar/baja se hacen desde Inventario según su acción y autorización.
7. Etiquetas F11 se generan desde recepción, carga inicial o ficha. Revisar vista previa; el diálogo Windows aparece solo al elegir imprimir. Cancelar no cambia stock. Verificar el lector/impresora físicos antes de calibrar tamaño.
8. Reportes resumen datos operativos existentes según permisos; no reemplazan contabilidad/facturación fiscal.

## Caja, respaldos y conectividad

Registrar movimientos de caja con concepto y medio correctos. Al cerrar, el arqueo es ciego: contar físicamente y luego consultar diferencias. No corregir históricos por fuera del flujo de compensación autorizado.

Respaldos locales y Drive aún requieren pruebas reales/manuales (F08). No asumir que una copia existe por ver un estado en pantalla. Conservar el código de recuperación separado de la PC; nunca enviarlo junto con la copia. Restaurar requiere comprobar destino, copia previa, código y caja cerrada. No borrar carpetas de usuario al desinstalar.

Las operaciones locales de la app no deben depender de Internet; Drive y actualizaciones sí. Si Internet falla, no repetir una operación financiera si el resultado es incierto: revisar ventas/caja antes de intentar nuevamente.

## Actualizaciones, PC dañada y soporte

La pantalla Actualizaciones se habilita solo para consulta en `--dev-db`; instalar está deshabilitado. En producción, solo la Dueña puede instalar una versión verificada y tras confirmar. El canal aún no está configurado.

Si la PC falla, no reinstalar sobre la única copia ni borrar `Data`, `Backups` o `Secrets`. Desconectar el uso comercial, registrar mensaje/versión y avisar a quien administra el proyecto. No compartir contraseñas, tokens, código de recuperación o archivos `.db` por canales abiertos. Seguir la guía de recuperación de `docs/CHECKLIST_PC_REAL.md`/`docs/PRUEBA_PILOTO.md`; la recuperación real de Drive y una PC nueva siguen pendientes.

Contacto de soporte: solicitarlo a quien administra el proyecto LocalInfantil; el proyecto aún no tiene un contacto comercial publicado.
