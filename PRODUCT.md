# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

## Users

VisualNotes está dirigido principalmente a estudiantes, investigadores y profesionales que necesitan convertir material visible en pantalla en apuntes reutilizables mientras estudian, investigan, asisten a una clase o reunión, o documentan un flujo de trabajo.

Su trabajo principal consiste en capturar información sin interrumpir la actividad en curso, agruparla en sesiones de trabajo, asignarla a `Curso > Módulo > Sección`, revisarla y convertirla en un documento estructurado.

## Product Purpose

VisualNotes convierte capturas de pantalla dispersas en apuntes estructurados, revisables y exportables. El producto debe permitir capturar con rapidez, conservar el contexto de cada elemento, organizar el material y preparar un documento final sin obligar al usuario a reconstruir manualmente lo que estaba viendo.

El éxito significa que una persona puede pasar de una sesión de captura a un documento útil y comprensible, manteniendo control sobre qué contenido se conserva, procesa, excluye y exporta.

## Positioning

La propuesta diferencial es un flujo continuo de captura, sesiones transversales a varios cursos, organización académica por curso, módulo y sección, análisis visual opcional mediante IA y composición documental. VisualNotes no se limita a almacenar imágenes: conserva su contexto, permite revisarlas y las transforma en una estructura de apuntes preparada para exportación.

## Operating Context

- Aplicación de escritorio nativa para Windows que puede permanecer en la bandeja del sistema.
- Captura predeterminada del monitor bajo el cursor, región persistente, ventana elegida o escritorio virtual, incluidos entornos con varios monitores y diferentes escalas DPI.
- Uso mediante ventana principal, panel flotante, atajos con foco y atajos globales.
- Organización local por sesiones de trabajo y destinos académicos `Curso > Módulo > Sección`; una sesión puede abarcar varios cursos.
- Revisión, clasificación, edición, deduplicación, reprocesado e inclusión o exclusión del contenido.
- Vista previa semántica y exportación prevista a documentos Word (`.docx`).
- Análisis visual opcional mediante OpenAI o Google Gemini, sujeto a configuración y consentimiento.

## Capabilities and Constraints

- Plataforma confirmada: Windows 10 2004 (build 19041) o posterior y Windows 11, actualmente solo en equipos x64.
- Implementación de escritorio con .NET 8 y WPF; el producto no es multiplataforma.
- Persistencia local mediante SQLite y archivos, con copias de seguridad y diagnósticos estructurados.
- Credenciales protegidas mediante Windows DPAPI y almacenadas fuera de las sesiones y copias de seguridad.
- La captura y la gestión local deben funcionar sin una clave de proveedor de IA.
- El envío de imágenes o texto a un proveedor externo requiere configuración y consentimiento explícito.
- El producto sigue en desarrollo y no tiene todavía una versión estable ni binarios firmados para uso general.
- La captura, revisión y exportación local están conectadas; el análisis remoto continúa siendo opcional y conserva áreas de integración en desarrollo según proveedor y configuración.
- El idioma inicial del producto es español.

## Brand Commitments

- El nombre del producto es **VisualNotes**.
- Deben preservarse el logotipo y el icono existentes en `src/VisualNotes.App/Assets/VisualNotesLogo.png` y `src/VisualNotes.App/Assets/VisualNotes.ico`.
- La voz inicial debe ser clara, directa y útil en español, especialmente al explicar privacidad, errores, estados pendientes y acciones que afectan al contenido.
- La privacidad local, el control del usuario y la accesibilidad son compromisos del producto, no características opcionales.

## Evidence on Hand

- Descripción funcional, requisitos, limitaciones y recorridos de uso en `README.md`.
- Arquitectura y flujo de datos en `docs/architecture.md`.
- Criterios manuales de accesibilidad en `docs/accessibility-manual-checklist.md`.
- Interfaz WPF existente en `src/VisualNotes.App`.
- Pruebas unitarias, de integración, arquitectura y UI en `tests`.
- Logotipo e icono existentes en `src/VisualNotes.App/Assets`.
- No hay todavía testimonios, métricas comerciales, precios, clientes publicados ni licencia pública que futuros trabajos puedan afirmar o inventar.

## Product Principles

1. **Capturar sin romper el ritmo.** Las acciones frecuentes deben ser rápidas y estar disponibles desde el contexto donde trabaja el usuario.
2. **Del material visual al conocimiento estructurado.** Cada captura debe conservar contexto y contribuir a un documento comprensible, no convertirse en otra imagen aislada.
3. **Control antes que automatización.** El usuario decide qué se procesa, comparte con proveedores externos, incluye, excluye y exporta.
4. **Local y recuperable por defecto.** Sesiones, capturas, configuración y credenciales deben permanecer protegidas y resistir cierres, pausas y fallos.
5. **Accesibilidad funcional.** Los recorridos completos deben poder entenderse y operarse con teclado, foco visible, lector de pantalla, contraste alto y escalado de Windows.

## Accessibility & Inclusion

- Todos los flujos principales deben ser operables con teclado y exponer nombres, roles, estados y cambios mediante UI Automation para Narrador o NVDA.
- El foco debe ser visible y seguir un orden coherente, sin trampas de teclado.
- Los estados no pueden depender únicamente del color.
- Texto y controles deben seguir siendo utilizables con escalado de Windows al 100 %, 150 % y 200 %, en contraste alto y en configuraciones multimonitor con distintos DPI.
- El texto normal debe mantener al menos 14 px efectivos; texto y controles deben respetar los objetivos de contraste documentados en `docs/accessibility-manual-checklist.md`.
