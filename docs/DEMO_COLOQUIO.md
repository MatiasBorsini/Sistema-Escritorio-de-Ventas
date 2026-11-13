# Demostración académica de LocalInfantil

## Preparación previa

En Windows, comprobar la compilación y elegir una carpeta exclusiva para la demostración. Usar una ruta nueva para SQLite, que no sea la base del local ni una base provisional histórica. No sobrescribir archivos existentes. Preparar manualmente, dentro de esa base, una usuaria ficticia, un producto con variante, precio y stock, y una caja de prueba. No registrar las credenciales en este repositorio.

Con la carpeta creada y una ruta nueva elegida:

```powershell
dotnet run --project src/LocalInfantil.Wpf/LocalInfantil.Wpf.csproj --configuration Debug -- --dev-db "C:\ruta-de-prueba\demo-localinfantil.db"
```

El primer arranque prepara el esquema de la base seleccionada y permite configurar la primera usuaria. Ensayar el recorrido completo antes del coloquio. Evitar Point real y la impresión física; usar vista previa si está disponible. Este procedimiento no se ha ejecutado durante la preparación del documento.

## Recorrido sugerido: 90 segundos

1. **0–10 s:** iniciar sesión con la usuaria ficticia y señalar la navegación principal.
2. **10–25 s:** buscar el producto preparado y mostrar su variante.
3. **25–35 s:** consultar el stock disponible.
4. **35–55 s:** agregar una unidad al carrito y confirmar una venta ficticia con un medio manual; la caja de prueba debe estar abierta.
5. **55–65 s:** mostrar la vista previa del comprobante interno o un reporte, aclarando que no es factura fiscal.
6. **65–80 s:** explicar WPF/MVVM, casos de uso, repositorios y SQLite; la venta y el movimiento de stock se registran dentro de una transacción.
7. **80–90 s:** mencionar que Point se integra por HTTP, con control de estados e idempotencia; su validación con cuenta y terminal reales está pendiente. No iniciar un cobro remoto.

## Antes de entrar

Comprobar resolución, conexión eléctrica, datos de prueba y visibilidad de los botones. Tener una alternativa de explicación sin ejecución. El proyecto no está habilitado para producción; no usar datos de clientes reales.
