# Arquitectura

## Principios

VisualNotes separa reglas de negocio, adaptadores de infraestructura y presentación.
La dirección deseada de dependencias es:

```text
VisualNotes.App ───────────────┐
        │                      v
        └────────────> VisualNotes.Core
                               ^
VisualNotes.Infrastructure ────┘
```

`Core` no debe referenciar `App` ni `Infrastructure`. La aplicación compone los
servicios concretos y la infraestructura implementa los contratos definidos en Core.
Las reglas se validan en `VisualNotes.ArchitectureTests`.

## Capas

### Core

Contiene sesiones, jerarquía académica, secciones, capturas, configuración por
niveles, composición de prompts/documentos y coordinación de casos de uso. Sus
servicios reciben contratos para persistencia, modelos de lenguaje y recorte de
imágenes, lo que permite probarlos sin WPF, red o disco real.

### Infrastructure

Implementa repositorios Entity Framework Core sobre SQLite, migraciones, archivos de
captura, copias de seguridad, credenciales DPAPI, proveedores OpenAI/Gemini,
observabilidad y exportación Open XML. `VisualNotesRuntime` es el límite de composición
que crea la base local y conecta repositorios con el coordinador.

### App

Es una aplicación WPF para Windows. Contiene vistas y view models, integra captura de
pantalla y atajos globales y mantiene el icono de bandeja. La UI delega persistencia y
reglas de dominio; no debería duplicarlas.

## Flujo principal

1. Al iniciar, se crea `%LOCALAPPDATA%\VisualNotes` y se aplican migraciones SQLite.
2. El usuario crea o reanuda una sesión de trabajo y selecciona el destino académico
   `Curso > Módulo > Sección`.
3. Un atajo o la UI solicita una captura. El adaptador resuelve el monitor bajo el
   cursor, la región persistente, una ventana elegida o el escritorio virtual y
   devuelve la imagen con metadatos físicos (modo, monitor, DPI y rectángulo).
4. La captura se asocia a la sesión y sección activas, se almacena y el pipeline
   prepara el análisis con la configuración
   efectiva y el prompt resuelto.
5. El proveedor devuelve una respuesta estructurada, que se normaliza y conserva para
   revisión y reintentos durables.
6. El árbol semántico permite incluir, excluir y ordenar contenido antes de exportar.

## Modelo de organización

La sesión y la jerarquía académica son ejes independientes:

```text
NoteSession 1 ──────── * NoteSection * ──────── 1 CourseModule
    │                         │                        │
    │                         │                        * ──── 1 Course
    │                         │
    │                         * Screenshot
    │
    └─ pausa, inicio/fin, exportación y documento previsto
```

- `NoteSession` representa un periodo de trabajo y es el límite de revisión,
  procesamiento por lotes y exportación.
- `Course` contiene módulos académicos.
- `CourseModule` pertenece a un curso.
- `NoteSection` pertenece a una sesión y referencia opcionalmente un curso y módulo.
  Por ello, dos secciones de una misma sesión pueden pertenecer a ubicaciones
  académicas distintas.
- `Screenshot` pertenece a la sesión y a la sección activa en el momento de captura.
  Los apuntes de texto usan la misma entidad sin `ScreenshotImage`.

`NoteSession.CourseId`, `CourseModuleId` y `Module` se mantienen como contexto inicial
y compatibilidad con datos anteriores. No deben usarse para imponer que toda la
sesión pertenezca a un único curso. Las decisiones nuevas deben tomar el contexto
académico desde `NoteSection`.

## Resolución del destino de captura

- **Pantalla:** `CurrentMonitor`; selecciona el monitor donde está el cursor.
- **Región:** `OneTimeRegion`; una selección nueva se persiste en píxeles físicos y
  las siguientes capturas la reutilizan. El bloqueo impide redefinirla.
- **Ventana:** la App enumera ventanas superiores visibles, excluye el proceso actual
  y pasa el identificador elegido en `CaptureRequest.WindowHandle`.
- **Todos los monitores:** `FullVirtualDesktop`; usa el rectángulo combinado de
  Windows, incluidos orígenes negativos.

El servicio oculta temporalmente las superficies WPF de VisualNotes antes de copiar
los píxeles y las restaura en un bloque `finally`. La selección de destino se resuelve
antes de ocultarlas, salvo que el modo necesite interacción previa mediante su
selector.

## Persistencia y datos sensibles

- SQLite conserva sesiones, secciones, metadatos, configuración y trabajos.
- Las imágenes y artefactos grandes se almacenan como archivos; la base mantiene sus
  referencias.
- Las migraciones son incrementales y se ejecutan al arrancar.
- `SectionAcademicContext` añade `CourseId` y `CourseModuleId` a las secciones y
  rellena ambos valores desde la sesión antigua. Es una migración hacia delante y no
  elimina capturas ni los campos históricos.
- Las claves API se protegen con DPAPI y no forman parte de exportaciones portables o
  copias de seguridad.
- Los logs y diagnósticos deben evitar secretos y minimizar contenido de usuario.

## Decisiones para cambios futuros

- Añadir un proveedor nuevo mediante el contrato de Core y un adaptador en
  Infrastructure; no introducir su SDK en Core.
- Añadir un formato de exportación detrás de un exportador dedicado.
- Mantener operaciones externas cancelables, con timeout y errores accionables.
- Versionar todo formato persistente y proporcionar migración hacia delante.
- Asociar contexto académico nuevo a `NoteSection`, no a `NoteSession`; la sesión
  debe seguir admitiendo varios cursos y módulos.
