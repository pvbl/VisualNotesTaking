# VisualNotes

Aplicación de escritorio para Windows que convierte capturas de pantalla en apuntes
estructurados. VisualNotes organiza el trabajo por sesiones y secciones, permite
capturar regiones persistentes o pantallas completas, enriquecer las capturas con un
modelo de lenguaje visual y revisar el documento antes de exportarlo.

> **Estado:** proyecto en desarrollo. No se publican todavía binarios firmados ni una
> versión estable. Las credenciales de proveedores externos deben configurarse en la
> aplicación y nunca almacenarse en el repositorio.

## Funcionalidades

- Captura del escritorio virtual, monitor actual, ventana activa o región persistente.
- Sesiones con secciones jerárquicas, pausa, reanudación y recuperación local.
- Clasificación, edición, deduplicación y reprocesado de capturas.
- Integración con OpenAI y Gemini mediante adaptadores HTTP resilientes.
- Vista previa semántica y exportación a Open XML (`.docx`).
- Persistencia local con SQLite, copias de seguridad y diagnósticos estructurados.
- Atajos globales, icono en la bandeja y soporte para varios monitores/DPI.

## Requisitos

| Requisito | Motivo |
|---|---|
| Windows 10/11 | La interfaz usa WPF y la captura/credenciales usan APIs de Windows. |
| .NET 8 SDK | Compilación, pruebas y herramientas locales. |
| Visual Studio 2022 (opcional) | Desarrollo y depuración de escritorio. |
| Clave de OpenAI o Gemini (opcional) | Solo para análisis real con un proveedor externo. |

## Inicio rápido

```powershell
git clone <url-del-repositorio>
cd VisualNotesTaking
dotnet tool restore
dotnet restore VisualNotes.sln
dotnet build VisualNotes.sln --configuration Release
dotnet run --project src/VisualNotes.App
```

Los datos se guardan bajo `%LOCALAPPDATA%\VisualNotes`, incluida la base SQLite y
los diagnósticos. Las claves se protegen mediante DPAPI. Para experimentar sin llamar
a servicios externos se pueden ejecutar todas las pruebas unitarias, que emplean
dobles o servidores HTTP simulados.

## Verificación local

```powershell
# Formato y analizadores
dotnet format VisualNotes.sln --verify-no-changes --severity error
dotnet build VisualNotes.sln --configuration Release /warnaserror

# Suite automatizada que no depende de UI ni servicios externos
dotnet test VisualNotes.sln --configuration Release `
  --filter "Category=Unit|Category=Architecture|Category=Integration"
```

Las pruebas UI requieren una sesión de escritorio de Windows. Consulte la
[estrategia de pruebas](tests/README.md) para filtros, snapshots, fixtures y la matriz
manual de captura.

## Calidad y métricas

El repositorio aplica analizadores de .NET, Meziantou y Sonar, warnings como errores
en `Release`/CI, reglas de arquitectura, cobertura Cobertura, mutation testing y
benchmarks. Los resultados se generan en CI como artefactos; **no se inventan ni se
mantienen porcentajes manuales en este README**, porque quedarían obsoletos.

La [guía de calidad](docs/quality.md) explica cómo obtener cobertura, mutation score,
resultados de tests y medidas de rendimiento, además de qué puertas son obligatorias.

## Documentación

- [Arquitectura y flujo de datos](docs/architecture.md)
- [Calidad, pruebas y métricas](docs/quality.md)
- [Guía detallada de pruebas](tests/README.md)
- [Cómo contribuir](CONTRIBUTING.md)

## Estructura

```text
src/
  VisualNotes.App/             WPF, vistas, view models y adaptadores Windows
  VisualNotes.Core/            dominio y casos de uso sin dependencias de UI
  VisualNotes.Infrastructure/  SQLite, archivos, proveedores, exportación y diagnóstico
tests/
  VisualNotes.UnitTests/       lógica y contratos aislados
  VisualNotes.IntegrationTests persistencia, archivos y exportación
  VisualNotes.ArchitectureTests límites entre capas
  VisualNotes.UiTests/         automatización de escritorio Windows
  VisualNotes.Benchmarks/      escenarios BenchmarkDotNet
```

## Seguridad y privacidad

- No incluya claves, capturas reales ni datos personales en commits o fixtures.
- Revise los paquetes de diagnóstico antes de compartirlos.
- Las llamadas a modelos pueden enviar imágenes y texto al proveedor configurado;
  use únicamente contenido cuya transferencia esté autorizada.
- Informe vulnerabilidades por un canal privado al equipo mantenedor, no mediante una
  incidencia pública.

## Licencia

El repositorio no contiene todavía un archivo de licencia. Hasta que se añada uno,
no debe asumirse permiso para redistribuir o reutilizar el código fuera de los
términos acordados con sus propietarios.
