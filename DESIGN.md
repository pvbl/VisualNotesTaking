---
name: VisualNotes
description: "Un cuaderno de campo digital sereno y técnico para capturar, revisar y estructurar conocimiento."
colors:
  archive-blue: "#172033"
  archive-panel: "#243149"
  ink-deep: "#334155"
  ink-medium: "#475569"
  text-muted: "#64748B"
  text-subtle: "#94A3B8"
  paper-cold: "#F4F6FA"
  panel-cold: "#F1F5F9"
  surface: "#FFFFFF"
  border: "#CBD5E1"
  surface-muted: "#E2E8F0"
  capture-cobalt: "#2563EB"
  capture-cobalt-deep: "#1D4ED8"
  capture-cobalt-soft: "#DBEAFE"
  review-teal: "#0F766E"
  success: "#166534"
  danger: "#B91C1C"
  warning-paper: "#FFF7ED"
  warning-ink: "#9A3412"
  section-cyan: "#7DD3FC"
  focus-ink: "#111827"
typography:
  display:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "32px"
    fontWeight: 600
  headline:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "22px"
    fontWeight: 600
  title:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "18px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "11px"
    fontWeight: 600
  mono:
    fontFamily: "Consolas, monospace"
    fontSize: "14px"
    fontWeight: 400
rounded:
  focus: "2px"
  compact: "6px"
  selector: "7px"
  status: "8px"
  card: "10px"
spacing:
  xxs: "4px"
  xs: "6px"
  sm: "8px"
  md: "10px"
  control: "12px"
  group: "18px"
  card-compact: "22px"
  card-roomy: "28px"
  page: "36px"
components:
  button-default:
    typography: "{typography.body}"
    padding: "6px 10px"
    height: "32px"
  button-primary:
    backgroundColor: "{colors.capture-cobalt}"
    textColor: "{colors.surface}"
    typography: "{typography.body}"
    padding: "8px 14px"
  button-review:
    backgroundColor: "{colors.review-teal}"
    textColor: "{colors.surface}"
    typography: "{typography.body}"
    padding: "10px 16px"
  card-standard:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-deep}"
    rounded: "{rounded.card}"
    padding: "{spacing.card-roomy}"
  card-compact:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-deep}"
    rounded: "{rounded.card}"
    padding: "{spacing.control}"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.archive-blue}"
    typography: "{typography.body}"
    height: "32px"
  nav-item:
    backgroundColor: "transparent"
    textColor: "{colors.surface}"
    typography: "{typography.body}"
    padding: "12px 14px"
  dock-choice:
    backgroundColor: "{colors.surface-muted}"
    textColor: "{colors.ink-medium}"
    rounded: "{rounded.selector}"
    width: "48px"
    height: "38px"
  warning:
    backgroundColor: "{colors.warning-paper}"
    textColor: "{colors.warning-ink}"
    rounded: "{rounded.compact}"
    padding: "{spacing.md}"
---

# Design System: VisualNotes

## Overview

**Creative North Star: "El cuaderno de campo digital"**

VisualNotes se comporta como una herramienta de trabajo que acompaña la captura, ordena la evidencia y deja el material listo para pensar. Su lenguaje visual es sereno, metódico y fiable: una estructura azul noche sostiene superficies claras, mientras los acentos funcionales indican cuándo actuar y cuándo revisar.

La interfaz es compacta y directa. La personalidad aparece en la precisión de la jerarquía, en la separación limpia de zonas de trabajo y en el uso disciplinado del color, no en decoración añadida. El resultado debe sentirse técnico sin volverse frío y denso sin convertirse en ruido.

**Key Characteristics:**

- Azul noche para la estructura permanente y papel frío para el área de trabajo.
- Componentes compactos, directos, serenos y técnicos.
- Jerarquía tipográfica sobria basada en Segoe UI y Consolas para Markdown.
- Profundidad plana construida con contraste tonal, bordes y agrupación.
- Estados accesibles mediante texto, foco visible y color con función estable.

## Colors

La paleta combina tinta azulada, papel frío y dos acentos funcionales con responsabilidades distintas.

### Primary

- **Cobalto de Captura:** identifica la acción principal, la selección activa y el contexto de captura.
- **Azul Archivo:** sostiene la navegación, la marca y la estructura permanente de la aplicación.

### Secondary

- **Verde Revisión:** acompaña el procesamiento, la revisión completa y los estados satisfactorios que requieren atención operativa.
- **Cian de Sección:** destaca la sección activa dentro de superficies oscuras; no sustituye al Cobalto de Captura como llamada a la acción.

### Neutral

- **Papel Frío:** fondo principal de las áreas de trabajo.
- **Panel Frío:** fondo de herramientas flotantes y zonas compactas.
- **Superficie Blanca:** tarjetas, editores y contenedores de contenido.
- **Tinta Profunda:** títulos y texto de alta jerarquía.
- **Texto Atenuado:** descripciones, metadatos y contexto secundario.
- **Borde Pizarra:** divisores, límites de campo y separación discreta.

### Named Rules

**The Two-Accent Rule.** El Cobalto de Captura inicia o selecciona; el Verde Revisión confirma, procesa o conduce a la revisión. No intercambiar sus funciones.

**The Cold Paper Rule.** Las zonas de trabajo parten de un gris azulado claro, no de gris neutro puro; las superficies blancas deben conservar una separación perceptible.

**The Accessible State Rule.** Error, éxito, advertencia, pausa, selección e inclusión siempre conservan una señal textual o estructural además del color.

## Typography

**Display Font:** Segoe UI (con respaldo sans-serif)
**Body Font:** Segoe UI (con respaldo sans-serif)
**Label/Mono Font:** Consolas para fuente Markdown editable o de solo lectura

**Character:** La tipografía es nativa de Windows, legible y sin gestos editoriales ajenos al producto. El contraste proviene de tamaño, peso y posición; la interfaz evita mezclar familias salvo cuando el contenido es código o Markdown.

### Hierarchy

- **Display** (Semibold, 32px): título principal de cada vista.
- **Headline** (Semibold, 22px): división mayor dentro de una vista, como sesiones recientes.
- **Title** (Semibold, 18-20px): encabezados de tarjetas, paneles y detalles.
- **Body** (Regular, 14px): controles, contenido y texto operativo; es también el mínimo efectivo establecido para el uso normal.
- **Label** (Semibold, 10-11px): rótulos compactos y metadatos de panel; las mayúsculas se reservan para pequeñas cabeceras de grupo.
- **Mono** (Regular, 14px): fuente y resultado Markdown donde el espaciado de caracteres debe permanecer estable.

### Named Rules

**The Native Clarity Rule.** Segoe UI es la voz de la aplicación; Consolas aparece únicamente cuando el contenido requiere lectura monoespaciada.

**The One Display Rule.** Cada superficie tiene un solo título de 30-32px; las subdivisiones descienden a 22px y 18-20px sin competir con él.

## Layout

La ventana principal usa una barra lateral fija de 220px y un área de contenido flexible con 36px de margen exterior. La navegación y el estado de sesión permanecen anclados, mientras el contenido cambia dentro de una única superficie de trabajo.

Las vistas combinan pilas verticales, tarjetas y divisiones proporcionales. Sesión usa una relación 2:3 entre lista y detalle; Revisión usa 3:2 entre cronología y editor. Los grupos principales se separan con 18-30px, y los controles relacionados con 4-12px. Las tarjetas amplias usan 22-28px de relleno; el panel flotante reduce esa densidad a 12px.

La adaptación depende del redimensionado nativo, columnas flexibles y `ScrollViewer`, no de breakpoints web. La ventana principal conserva un mínimo de 800×500px. El Centro de captura tiene una anchura inicial de 430px, puede reducirse hasta 240px y mantiene un modo mínimo accesible.

**The Fixed Shell Rule.** La barra lateral establece orientación persistente; las tareas viven en el área flexible y no crean una segunda navegación paralela.

**The Compact Tool Rule.** Paneles flotantes y barras de acción reducen espacio, pero nunca el tamaño mínimo efectivo de los controles ni el recorrido de teclado.

**The Two-Axis Context Rule.** La sesión de trabajo y la ruta académica son contextos distintos. El Centro de captura muestra primero la sesión y después `Curso › Módulo › Sección`; nunca presenta curso o módulo como límites de toda la sesión.

## Elevation & Depth

El sistema es plano y tonal. No hay sombras declaradas: la profundidad se comunica mediante el Azul Archivo de la carcasa, el Papel Frío del lienzo, las superficies blancas, bordes de un píxel y agrupaciones con radio. Las capas flotantes conservan un borde oscuro para permanecer legibles sobre escritorios variables.

**The Flat-by-Structure Rule.** Una superficie gana jerarquía por contraste, borde y posición; no se añaden sombras para compensar una agrupación débil.

## Shapes

Las tarjetas principales usan curvas suaves de 10px. Estados, avisos y paneles interiores bajan a 6-8px; selectores compactos usan 7px. El contorno de foco conserva una curva mínima de 2px para permanecer nítido y visible.

Los controles WPF mantienen su silueta nativa salvo que una variante funcional establezca una forma explícita. La geometría debe sentirse contenida y técnica: no convertir botones, campos o tarjetas rectangulares en píldoras.

**The Nested Radius Rule.** El contenedor exterior usa el radio mayor; los elementos interiores emplean radios iguales o menores.

## Components

### Buttons

Compactos y directos; el texto describe la acción sin depender del icono.

- **Shape:** silueta nativa de WPF, con altura mínima de 32px y relleno base de 10×6px.
- **Primary:** Cobalto de Captura sobre blanco, con 14×8px de relleno en acciones de captura o creación.
- **Review:** Verde Revisión sobre blanco, con peso Semibold y 16×10px de relleno.
- **Focus:** contorno Tinta de Foco de 3px, separado 3px del límite para conservar visibilidad independiente del color.
- **Secondary:** superficie y estados nativos de Windows; no compiten cromáticamente con las acciones Cobalto o Verde.

### Cards / Containers

Serenos y técnicos; agrupan una tarea completa, no cada fragmento de texto.

- **Corner Style:** curva de tarjeta de 10px.
- **Background:** Superficie Blanca sobre Papel Frío o Panel Frío.
- **Shadow Strategy:** ninguna; consultar Elevation & Depth.
- **Border:** solo cuando la tarjeta vive dentro de otra superficie blanca o representa una credencial, fila o límite interactivo.
- **Internal Padding:** 22-28px en vistas principales y 12px en el panel flotante.

### Inputs / Fields

- **Style:** campos WPF blancos con altura mínima de 32px, texto Segoe UI de 14px y bordes nativos.
- **Focus:** contorno oscuro de 3px que no depende del color del campo.
- **Multiline:** crece según la tarea y habilita desplazamiento cuando el contenido es Markdown o instrucciones.
- **Error / Disabled:** el mensaje permanece visible como texto; el color rojo es un refuerzo y no la única señal.

### Navigation

La navegación lateral usa texto blanco de 15px, alineado a la izquierda, sobre Azul Archivo. Cada elemento dispone de 14×12px de relleno y 2px de separación vertical. El estado de sesión se aloja al pie en un panel azul más claro, separado de la lista de destinos.

### Capture Timeline Row

La fila de captura combina una miniatura fija de 100×68px, un bloque flexible de título y metadatos, y un área de estado alineada a la derecha. Los metadatos incluyen la ruta académica completa `Curso › Módulo › Sección` para distinguir títulos repetidos. Las filas se separan con un borde inferior; la jerarquía no depende de tarjetas individuales.

### Capture Target Controls

El selector nombra los modos con lenguaje de usuario: **Pantalla**, **Región**, **Ventana** y **Todos los monitores**. Pantalla es el valor inicial. Región acompaña el selector con estado textual y acciones separadas para definir/redefinir y bloquear/desbloquear; una acción no cambia de significado sin actualizar también su etiqueta accesible.

### Dock Choice

Selector compacto de 48×38px con radio de 7px. El estado normal usa Superficie Atenuada y Borde Pizarra; la selección cambia a Cobalto Suave, borde Cobalto y texto Cobalto Profundo.

### Warning

Aviso compacto sobre Papel de Advertencia, con Tinta de Advertencia, radio de 6px y 10px de relleno. Permanece dentro del flujo y cerca de la acción afectada.

## Do's and Don'ts

### Do:

- **Do** mantener la distribución Azul Archivo → Papel Frío → Superficie Blanca para distinguir carcasa, espacio de trabajo y contenido.
- **Do** usar Cobalto de Captura para iniciar o seleccionar y Verde Revisión para procesar, confirmar o abrir una revisión.
- **Do** conservar títulos de vista de 30-32px, cuerpo efectivo de al menos 14px y foco visible de 3px.
- **Do** agrupar tareas completas en tarjetas de 10px y separar filas repetidas con divisores ligeros.
- **Do** mantener texto, icono o estructura accesible para cada estado comunicado con color.

### Don't:

- **Don't** añadir sombras a tarjetas o paneles cuando el contraste tonal y la agrupación ya expresan la jerarquía.
- **Don't** convertir controles rectangulares en píldoras ni introducir radios mayores que la tarjeta contenedora.
- **Don't** usar Cobalto y Verde como acentos decorativos intercambiables.
- **Don't** introducir otra familia tipográfica para decorar encabezados; Segoe UI y Consolas cubren las funciones existentes.
- **Don't** envolver cada fila, campo o bloque de texto en una tarjeta independiente.
