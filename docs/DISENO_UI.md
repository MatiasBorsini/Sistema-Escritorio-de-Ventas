# LocalInfantil — Sistema de diseño de interfaz

**Estado:** especificación visual aprobada como punto de partida por la persona responsable del proyecto. **Versión:** 1.0. **Alcance:** pantallas WPF existentes y futuras, sin modificar reglas comerciales ni habilitar funciones inexistentes.

## 1. Propósito y criterio rector

LocalInfantil es una aplicación de escritorio para gestionar una tienda de ropa infantil. Debe transmitir calidez, claridad, calma y profesionalismo; no parecer un juguete ni una herramienta técnica destinada exclusivamente a programadores. El usuario debe comprender qué puede hacer, qué falta completar, qué se guardó y qué salió mal.

El foco visual son **los datos y la acción principal de cada tarea**. Evitar fondos blancos extensos, saturación, decoración excesiva, animaciones distractoras y controles apretados. Diseñar primero para uso diario con mouse y teclado en Windows; no sacrificar legibilidad por imitar una aplicación web.

### Nivel de aprobación

- **Aprobado:** estilo general del maquetado conversado, menú horizontal superior con submenús, menú de perfil a la derecha y «Cerrar sesión» dentro de él, paleta base, tipografía Segoe UI, lenguaje en español claro, coherencia entre pantallas.
- **Orientativo, sujeto a validación visual:** tamaño exacto de componentes, iconografía, distribución interna por pantalla y detalles del maquetado generado. Los textos, cantidades, ilustraciones, gráficos y registros ficticios del maquetado NO prueban que exista una función.
- **No aprobado ni implementado por esta especificación:** desarrollo de ventas, caja, clientes, reportes, respaldos, cambios en permisos o alteraciones de lógica comercial.

## 2. Paleta y tokens semánticos

Definir estos colores una vez en recursos WPF. Usar claves semánticas en las vistas, no valores hexadecimales repetidos en cada pantalla.

| Uso / token sugerido | HEX | Aplicación |
| --- | --- | --- |
| `Color.Nav` | `#263D43` | Barra superior, fondo de menús y navegación. |
| `Color.Canvas` | `#F2F1EC` | Fondo general, sin blanco absoluto dominante. |
| `Color.Surface` | `#FFFDF9` | Formularios, tarjetas, tablas, paneles. |
| `Color.Primary` | `#286B66` | Acción principal, selección y estados activos. |
| `Color.Accent` | `#D9A68C` | Detalles decorativos puntuales, no texto pequeño ni acciones destructivas. |
| `Color.Text` | `#26343B` | Texto principal y encabezados. |
| `Color.TextSecondary` | `#52636A` | Descripciones y etiquetas secundarias sobre superficies claras. |
| `Color.Border` | `#CBD4CF` | Límites suaves entre campos, tablas y tarjetas. |
| `Color.SuccessBg` | `#E6F3EA` | Fondo para confirmaciones correctas. |
| `Color.SuccessText` | `#205C3F` | Texto de éxito sobre `SuccessBg`. |
| `Color.InfoBg` | `#E8F2FA` | Fondo informativo. |
| `Color.InfoText` | `#244C70` | Texto informativo sobre `InfoBg`. |
| `Color.WarningBg` | `#FFF2D9` | Advertencia que requiere atención. |
| `Color.WarningText` | `#785016` | Texto de advertencia sobre `WarningBg`. |
| `Color.ErrorBg` | `#FBEAEA` | Fondo de errores. |
| `Color.ErrorText` | `#922D32` | Texto de error sobre `ErrorBg`. |
| `Color.Destructive` | `#922D32` | Acciones destructivas, con confirmación específica. |

**Reglas:** priorizar contraste para texto y controles en su estado real (normal, hover, seleccionado, deshabilitado y foco). Verificar WCAG AA como objetivo práctico: texto normal ≥ 4,5:1, texto grande ≥ 3:1 e indicadores visuales significativos ≥ 3:1. Si un par falla, ajustar su color semántico y documentar la corrección; no asumir conformidad por usar estos HEX. En la barra oscura usar texto claro legible. No usar `Color.Accent` como texto pequeño. No comunicar errores, permisos o stock solamente con colores: acompañar con texto e iconos accesibles.

El rojo se reserva para errores y destrucción; el amarillo para advertencias; el verde de marca para acción principal, **no** como sustituto de una etiqueta de estado. Mantener la señalización visible de **modo desarrollo** y de la ruta de base activa sin convertirla en un adorno.

## 3. Tipografía, ritmo y componentes

- **Familia:** Segoe UI / tipografía del sistema Windows, con alternativa estándar si faltara.
- **Escala inicial orientativa en puntos WPF (DIP):** título de página 24; título de bloque 18; contenido 14–15; etiqueta 13–14; ayuda contextual 12–13. No fijar tamaños inferiores a 12 para datos esenciales. Ajustar después de comprobar DPI y escalado.
- **Espaciado:** base 8 DIP; 4 entre elementos estrechamente relacionados, 12–16 entre campos, 24–32 entre secciones. Preferir espacios y alineación a divisores decorativos.
- **Controles:** alto cómodo y consistente; radio de esquina discreto; borde y foco visibles. Limitarse a una acción primaria destacada por pantalla o paso.
- **Botones:** primario (verde), secundario (superficie con borde), peligro (rojo con confirmación), texto (acciones menores). Distinguir deshabilitado y ocupado; bloquear doble confirmación.
- **Formularios:** etiqueta persistente encima o junto al campo; marcar requeridos, formato aceptado, ejemplos y error junto al campo. No depender del placeholder como única etiqueta.
- **Tablas:** encabezados claros, números alineados, selección perceptible, paginación real donde ya existe, estados vacíos útiles y acciones identificables.
- **Feedback:** éxito específico (qué cambió), error comprensible y recuperable, confirmación para bajas/reversiones, progreso o estado ocupado sin falsos éxitos.
- **Accesibilidad:** navegación y activación mediante teclado, orden de tabulación natural, foco visible, contraste, etiquetas accesibles para botones de iconos y soporte de escalado de Windows.

## 4. Estructura general y navegación

**Barra superior horizontal** persistente, fondo `Color.Nav`:

- Izquierda: identidad «LocalInfantil».
- Centro/izquierda: menús de tareas agrupados con submenús desplegables, sin una fila infinita de pestañas.
- Derecha: nombre del usuario y menú **Perfil ▼**, con «Cerrar sesión». «Mi cuenta» o «Cambiar contraseña» solo si la pantalla o el flujo existen de verdad.
- Área inferior: título de pantalla, ayuda corta opcional, acción primaria, filtros/tabla/formulario y zona de mensajes. No reemplazar estados comerciales con una pantalla decorativa.

**Mapa objetivo, condicionado a existencia real de funcionalidades:**

| Menú | Submenús previstos | Regla de disponibilidad |
| --- | --- | --- |
| Inicio | Vista inicial y accesos a tareas implementadas | No inventar métricas, alertas ni resumen del día. |
| Catálogo | Productos, variantes, precios | Acciones solo si están implementadas y autorizadas. |
| Compras | Proveedores, recepción, consultas/reversiones | No mostrar rutas inexistentes como operativas. |
| Inventario | Existencias, carga inicial, conteos, no vendible, historial; ajustes si hay interfaz | Mantener significado de existencias físicas/disponibles/apartadas. |
| Usuarios / Administración | Cuentas y permisos | Acceso de Dueña según permisos existentes; rótulos humanos. |
| Ventas, Caja, Clientes, Reportes, Configuración | Futuras agrupaciones | Ocultar hasta contar con flujo real; no habilitar botones vacíos. |
| Perfil | Identidad actual; cerrar sesión; opciones reales de cuenta | Menú extremo derecho, no botón «Cerrar sesión» suelto. |

No ocultar errores de autorización: el backend decide el permiso. La navegación puede reflejar disponibilidad, pero ocultar un botón NO constituye seguridad. No conceder permisos automáticamente. Para una Dueña nueva, explicar en lenguaje simple que puede configurar permisos comerciales explícitamente desde Administración.

**Estados:** distinguir `Disponible ahora`, `Existe sin pantalla`, `Planificado`. Este documento es un mapa de diseño, no una declaración de funcionalidades terminadas.

## 5. Plantillas de pantalla

### Inicio de sesión / creación de Dueña

Una columna central legible; título y ayuda breves; usuario y contraseña claramente etiquetados; errores sin filtrar secretos; advertencia de modo desarrollo separada visualmente. La primera creación explica por qué posteriormente deberá gestionar permisos. No enseñar contraseñas ni ofrecer recuperación inexistente.

### Pantalla inicial

Mostrar bienvenida, cuenta activa y accesos rápidos **solo a tareas existentes**. Las tarjetas de métricas del maquetado son ilustrativas: no generar números ficticios que aparenten datos reales. Si todavía no existe dashboard, usar un inicio mínimo y honesto.

### Catálogo

Título y descripción; búsqueda por nombre/SKU; lista con paginación y estados; acción visible «Nuevo producto». Organizar ficha, variantes y precios sin transformar cada operación en un menú técnico. Diferenciar `crear variante`, `fijar precio` y `cargar existencia inicial`.

### Compras y recepción

Proveedor, comprobante opcional y filas de variantes/cantidades/costos cuando existan. Explicar que **recibir SUMA existencias**, no reemplaza el stock. Confirmación clara y prevención de doble clic/reintentos.

### Inventario

El stock por SKU se expresa con tres cantidades y sus nombres completos: **Físico = Disponible + Apartado**. Apartar/reincorporar cambian clasificación, no físico. Baja definitiva descuenta físico y apartado tras confirmación. Conteos cuentan físico e incluyen apartadas. `Carga inicial = ESTABLECER cantidad inicial`, no `SUMAR ingreso`. Presentar historial y motivos de forma comprensible; no mezclar stock vendible con físico.

### Usuarios y permisos

Listado de usuarios y panel de permisos por persona. Mostrar nombres humanos («Gestionar productos», «Gestionar variantes», «Fijar precios», «Cargar stock inicial», «Gestionar prendas apartadas», «Autorizar baja definitiva») con ayuda corta; conservar identificadores técnicos exclusivamente en código y documentación técnica. Explicar «denegado por defecto» sin dar a entender que el sistema está averiado. Separar administración de permisos de acciones comerciales. Las opciones de cuenta aún no implementadas no deben simularse.

## 6. Patrones de texto

- Preferir «No tenés permiso para cargar stock inicial. La Dueña puede habilitarlo desde Administración → Permisos» a «Acceso denegado» genérico, siempre que sea exacto y no revele datos privados.
- Preferir «Se registró la recepción y se sumaron 5 unidades» a «Operación exitosa» genérico, si el dato real está disponible.
- Preferir «Esta prenda está apartada: no está disponible para vender» a una marca de color sin contexto.
- Motivos obligatorios, errores y confirmaciones se anuncian antes de escribir; nunca comunicar éxito antes de confirmar la transacción.
- Español claro, consistente y cercano, con voseo cuando corresponda; importes ARS y formatos reales sin redondeo silencioso.

## 7. Implementación WPF: contrato de reutilización

Crear o adaptar recursos compartidos (por ejemplo, `ResourceDictionary`) para brushes semánticos, estilos de TextBlock/Label, Button, TextBox, ComboBox, Tabular/ListBox, panel, mensajes y foco. Elegir nombres de clave estables y verificar que se cargan en ambas shells y diálogos. Evitar hardcodear colores, márgenes y tamaños en nuevas vistas salvo excepciones justificadas; migrar gradualmente las existentes, sin rediseño masivo no aprobado. No agregar frameworks de UI o paquetes nuevos sin evaluación y aprobación.

Para cada pantalla, contrastar con este documento, comprobar teclado/escalado/mensajes/vacío/permiso, y probar el recorrido real con una base ficticia. Separar defectos visuales de defectos comerciales: una mejora de presentación no puede modificar persistencia, permisos, conteos o stock sin otro alcance aprobado.

## 8. Control de cambios y aceptación

Cuando haya una discrepancia entre maqueta y producto, describirla: si es dato de ejemplo, función futura o decisión visual pendiente. Antes de dar por aprobada una interfaz, enseñar una captura o ejecutarla realmente en Windows y pedir validación del usuario. No afirmar prueba visual solo por compilar. No declarar el sistema listo para producción.

**Seguridad de datos de desarrollo:** el archivo provisional real previamente dañado no se abre, migra, reescribe, reconstruye ni usa para pruebas. Usar exclusivamente bases ficticias nuevas o una base de prueba existente con autorización explícita.

**Próximo hito:** validar navegación/cabecera/perfil y recursos WPF compartidos con una pantalla piloto; después aplicar progresivamente el diseño a las funciones existentes. La presente especificación NO implementa esos cambios.
