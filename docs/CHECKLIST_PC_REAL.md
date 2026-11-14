# Checklist de PC real

Completar en la computadora del local, con autorización de la Dueña y antes de cargar datos reales. Registrar únicamente información técnica necesaria; no guardar usuario Windows, número de serie, nombre del equipo, rutas personales, redes, tokens, contraseñas o datos comerciales en el informe compartido. F00-T02 y F10-T04 siguen pendientes hasta entonces.

## Inventario técnico mínimo (PowerShell)

Guardar solo los campos seleccionados en un archivo aprobado por la Dueña:

```powershell
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber, OsArchitecture, CsSystemType, CsNumberOfLogicalProcessors, CsTotalPhysicalMemory
Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors
Get-Printer | Select-Object Name, DriverName, PortName, PrinterStatus
Get-PSDrive -PSProvider FileSystem | Select-Object Name, Used, Free
dotnet --list-runtimes
```

No publicar la salida completa de `Get-ComputerInfo`, que puede incluir identificadores personales/dispositivo. Anotar manualmente fabricante/modelo de lector si lo hubiera sin serial individual.

## Puerta de compatibilidad

- [ ] Confirmar edición, versión y build exactas de Windows; revisar la tabla oficial vigente de plataformas soportadas .NET 10. El soporte de .NET 10 para Windows 10 es limitado a Windows 10 LTSC/Enterprise enumerados por Microsoft; no inferir soporte oficial solo porque un EXE arranque.
- [ ] Confirmar x64/x86/Arm64 del sistema, requisitos de CPU/RAM y espacio libre. No seleccionar `win-x64` hasta verificarlo.
- [ ] Probar instalación por usuario y permisos normales, acceso directo, arranque y cierre.
- [ ] Probar DB nueva aislada y migración desde copia temporal; medir duración y RAM con herramientas aprobadas. Nunca usar la base real para ensayos.
- [ ] Probar búsqueda/venta ficticia, medios/vuelto, caja, cierre y reinicio en el entorno controlado.
- [ ] Probar backup local, integridad y recuperación aislada; después evaluar OAuth/Drive real y una PC nueva (F08).
- [ ] Probar impresora/driver, preview/PDF/cancelación, lector tipo teclado y etiquetas Code128; registrar legibilidad y calibración.
- [ ] Probar actualización solo con canal y clave productiva aprobados en D-46, caja cerrada y una build de ensayo; verificar que una cancelación conserva la versión previa y los datos.
- [ ] Registrar RAM/arranque/respuesta medidos con método, resolución y escenario; no completar con impresiones subjetivas.
- [ ] Revisión fiscal/procedimiento de Dueña/contador, permisos D-01/D-03 y condición de uso real.

## Registro

Fecha / responsable: ________  Modelo técnico (sin serial): ________  Windows edition/version/build: ________  Arquitectura: ________  RAM: ________  CPU: ________  Espacio: ________  Impresora/driver: ________  Lector: ________

Resultado por punto y evidencia guardada en ubicación aprobada: ______________________________

No habilitar producción si queda algún punto bloqueante o si backup/restore, migración, fiscalidad, permisos o hardware siguen sin aprobación.
