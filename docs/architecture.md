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

Contiene sesiones, secciones, capturas, configuración jerárquica, composición de
prompts/documentos y coordinación de casos de uso. Sus servicios reciben contratos
para persistencia, modelos de lenguaje y recorte de imágenes, lo que permite probarlos
sin WPF, red o disco real.

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
2. El usuario crea o reanuda una sesión y selecciona una sección.
3. Un atajo o la UI solicita una captura; el adaptador Windows devuelve imagen y
   metadatos físicos (monitor, DPI y rectángulo).
4. La captura se almacena y el pipeline prepara el análisis con la configuración
   efectiva y el prompt resuelto.
5. El proveedor devuelve una respuesta estructurada, que se normaliza y conserva para
   revisión y reintentos durables.
6. El árbol semántico permite incluir, excluir y ordenar contenido antes de exportar.

## Persistencia y datos sensibles

- SQLite conserva sesiones, secciones, metadatos, configuración y trabajos.
- Las imágenes y artefactos grandes se almacenan como archivos; la base mantiene sus
  referencias.
- Las migraciones son incrementales y se ejecutan al arrancar.
- Las claves API se protegen con DPAPI y no forman parte de exportaciones portables o
  copias de seguridad.
- Los logs y diagnósticos deben evitar secretos y minimizar contenido de usuario.

## Decisiones para cambios futuros

- Añadir un proveedor nuevo mediante el contrato de Core y un adaptador en
  Infrastructure; no introducir su SDK en Core.
- Añadir un formato de exportación detrás de un exportador dedicado.
- Mantener operaciones externas cancelables, con timeout y errores accionables.
- Versionar todo formato persistente y proporcionar migración hacia delante.
