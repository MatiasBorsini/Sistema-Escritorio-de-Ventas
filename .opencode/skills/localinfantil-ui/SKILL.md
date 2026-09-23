---
name: localinfantil-ui
description: Aplicar y revisar el diseño de la interfaz WPF de LocalInfantil al crear, modificar o evaluar pantallas, navegación, menús, formularios, colores, permisos visibles o experiencia de usuario.
---

# Skill: interfaz WPF LocalInfantil

## Activación

Usar esta skill cuando la tarea afecte UI/UX de LocalInfantil: XAML, vistas, ViewModels con efecto visual, recursos, menú superior, Perfil, flujos de navegación, formularios, tablas, mensajes o maquetado. Para tareas puramente de persistencia o lógica sin interfaz, no forzar cambios visuales.

## Fuente de verdad

1. Localizar la raíz del repositorio y **leer completo** `docs/DISENO_UI.md` antes de proponer o editar interfaz. Ese archivo define la paleta, jerarquía visual, navegación y estados; esta skill NO es una copia del manual.
2. Leer la implementación WPF existente y su estilo compartido. Revisar ambas shells, recursos, dependencias, menús y comandos relevantes antes de modificar archivos.
3. Si falta el manual, informar el problema y detener decisiones de diseño arbitrarias; no inventar una segunda paleta.

## Procedimiento

1. Identificar el objetivo del usuario y clasificar cada elemento de la maqueta como **función existente**, **función existente sin interfaz**, **función futura** o **decoración de ejemplo**. No convertir ejemplos en datos reales ni añadir enlaces a pantallas inexistentes.
2. Proponer el mínimo cambio de UI necesario y explicitar qué está aprobado y qué requiere aprobación. Primero estructura de navegación y patrones globales; después una pantalla piloto; luego rediseño por módulos.
3. Reutilizar claves semánticas de recursos WPF; si falta un recurso compartido, proponerlo en un lugar central. Evitar HEX, tamaños y márgenes arbitrarios por pantalla.
4. Poner acciones del usuario en lugares previsibles: barra superior con submenús; Perfil a la derecha con Cerrar sesión; una acción primaria por vista; mensajes claros; nomenclatura humana para permisos.
5. Comprobar estados normal, hover, foco, teclado, ocupado, error, vacío, sin permiso y confirmación destructiva; verificar que no haya controles recortados con DPI o ventana moderada.
6. Respetar las invariantes de negocio y las autorizaciones del backend. Mejorar el mensaje de un permiso no equivale a concederlo. No tocar SQLite real o provisional ni realizar operaciones destructivas de prueba.
7. Si hay cambios: compilar y ejecutar pruebas pertinentes. Probar interactuando con WPF solo si existe escritorio disponible; reportar con honestidad qué se verificó y qué queda pendiente.

## Lista de aceptación en cada entrega visual

- [ ] Consulté `docs/DISENO_UI.md` y reutilicé sus tokens y términos.
- [ ] La pantalla tiene jerarquía clara y el usuario sabe cuál es la acción principal.
- [ ] Menú, Perfil, permisos y navegación son consistentes; no hay pantallas falsas.
- [ ] Colores y estados comunican sin depender solamente del color; texto y foco son legibles.
- [ ] Formularios, tablas y mensajes contemplan errores, validación, carga y contenido vacío.
- [ ] No alteré reglas comerciales, permisos ni bases fuera del alcance.
- [ ] Distinguí compilación, pruebas automatizadas y prueba interactiva real.

Ante un conflicto de diseño, pedir aprobación y actualizar **solo el manual** como fuente de verdad; mantener esta skill como proceso y referencia.
