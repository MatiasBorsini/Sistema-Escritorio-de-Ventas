# Instrucciones para OpenCode — Integración UX/UI LocalInfantil

## Objetivo
Implementar en WPF el rediseño visual aprobado de LocalInfantil usando los HTML adjuntos SOLO como referencia visual/UX. No convertir la app a web y no incrustar HTML/WebView.

## Archivos de referencia
- `LocalInfantil-prototipo-v5.html`: referencia visual para la aplicación principal.
- `LocalInfantil-login-compacto.html`: referencia visual para la ventana de inicio de sesión independiente.

## Reglas generales
1. Mantener la arquitectura y lógica de negocio existentes.
2. No cambiar persistencia, esquema SQLite, servicios ni reglas de negocio salvo que sea estrictamente necesario para conectar UI existente.
3. No inventar campos de negocio que hoy no existen.
4. Mantener la navegación principal ya existente.
5. Usar recursos/tokens compartidos para colores, bordes, tipografía, espaciado, botones, inputs, tablas y estados.
6. Evitar HEX arbitrarios repetidos en cada pantalla.
7. Priorizar experiencia de escritorio WPF: teclado, foco, doble clic, ventanas secundarias, densidad de información y buen uso del espacio.
8. Soportar DPI y redimensionado razonable de ventana.
9. No mostrar IDs técnicos/GUID como dato principal cuando exista una referencia humana.
10. No tocar bases históricas/provisionales ni datos reales durante pruebas; usar únicamente la base de desarrollo/prueba configurada para el proyecto.
11. No hacer commit ni push hasta recibir autorización explícita.

## Dirección visual
Paleta base:
- Navegación: #263D43
- Fondo: #F2F1EC
- Superficie: #FFFDF9
- Primario: #286B66
- Acento: #D9A68C
- Texto: #26343B
- Texto secundario: #52636A
- Borde: #CBD4CF

Mantener un look sobrio, cálido, claro y profesional. No convertir todo en tarjetas. La UI debe parecer una aplicación de escritorio, no un dashboard web.

## Login
Implementar como `LoginWindow` separada y compacta.
- No debe mostrar la navegación principal.
- Contener solamente marca, usuario, contraseña, recordar usuario, botón Ingresar y estado de sesión local.
- Al iniciar sesión correctamente, cerrar LoginWindow y abrir MainWindow.
- Tomar `LocalInfantil-login-compacto.html` como referencia visual.

## Catálogo / Productos
La pantalla principal debe priorizar búsqueda y listado de productos.
- Mantener lista densa con búsqueda, filtros y paginación.
- Mantener una vista rápida lateral compacta del producto seleccionado para no dejar espacio vacío.
- La vista rápida NO debe ser el formulario completo de edición.
- Doble clic sobre producto (y botón “Editar seleccionado”) debe abrir una ventana secundaria de ficha de producto.
- Esa ventana contiene:
  - datos generales,
  - variantes,
  - estado/acciones.
- Para catálogos grandes, usar paginación/consulta eficiente; no cargar miles de productos en un único listado.
- No inventar campos comerciales nuevos.

## Recepciones
- El historial debe ocupar algo más de la mitad del ancho disponible.
- El detalle rápido de la recepción seleccionada debe ir a la derecha, más angosto y vertical.
- Mostrar solo resumen útil en el panel derecho.
- Incluir “Ver detalle completo” para abrir una ventana secundaria con tabla completa de productos/variantes de esa recepción.
- La ventana de detalle completo puede incluir búsqueda interna.
- La reversión debe estar separada visualmente de la consulta habitual y pedir motivo.
- Conservar reglas de negocio existentes de recepción y reversión.
- Recepción SUMA existencias; no confundir con carga inicial, que ESTABLECE.

## Proveedores
- Lista + ficha.
- Mantener únicamente los datos soportados por el sistema actual.
- No agregar CUIT, teléfono, domicilio, etc. si el dominio no los tiene.
- Desactivar en lugar de borrar cuando corresponda.

## Inventario
- Consulta por SKU.
- Mostrar Físico / Disponible / Apartado con jerarquía clara.
- Acciones contextuales: Apartar, Reincorporar, Baja definitiva.
- Mostrar solo el formulario de la acción elegida.
- Historial en panel lateral o inferior según espacio.
- Conservar: Físico = Disponible + Apartado.

## Conteo físico
Implementar como flujo progresivo:
1. Seleccionar variantes.
2. Registrar conteo.
3. Revisar diferencias.
4. Aprobar.
No mostrar todas las etapas como formularios gigantes simultáneamente.

## Usuarios
- Lista de usuarios + ficha del seleccionado.
- Separar permisos de acciones de seguridad.
- Restablecer contraseña y desactivar como acciones secundarias/sensibles.
- Mantener validación real de permisos en backend; ocultar botones no reemplaza autorización.

## Proceso de implementación
1. Revisar el código WPF actual y localizar Views, ViewModels, recursos y estilos existentes.
2. Proponer brevemente qué archivos se van a modificar y por qué.
3. Crear/ajustar un ResourceDictionary común para tokens y componentes visuales reutilizables.
4. Implementar primero Login y shell/navegación.
5. Implementar Catálogo y Recepciones respetando los patrones de ventana secundaria.
6. Implementar Proveedores, Inventario, Conteo físico y Usuarios.
7. Compilar.
8. Ejecutar toda la suite de tests existente.
9. Corregir warnings/errores introducidos por el cambio.
10. Mostrar un resumen de archivos modificados, resultado de build/tests y cualquier diferencia inevitable respecto al HTML.
11. NO hacer commit/push hasta autorización explícita.

## Importante
Los HTML son una especificación visual y de interacción. No copies estructuras web literalmente. Traducí los patrones a controles WPF/XAML adecuados (DataGrid/ListView, Grid, UserControl, Window/Dialog, Commands, estilos, triggers, etc.) respetando MVVM y la arquitectura actual.
