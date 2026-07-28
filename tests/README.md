# Pruebas de VisualNotes

## Proyectos y categorías

| Proyecto | Traits `Category` | Propósito |
|---|---|---|
| `VisualNotes.UnitTests` | `Unit` | Lógica pura; se mantiene paralela. |
| `VisualNotes.IntegrationTests` | `Integration`, opcionalmente `External`/`Slow` | SQLite, sistema de archivos y adaptadores. Cada prueba obtiene una base y carpeta exclusivas. |
| `VisualNotes.ArchitectureTests` | `Architecture` | Límites entre capas. |
| `VisualNotes.UiTests` | `UI`, `Windows`, opcionalmente `Slow` | Automatización de escritorio en un job Windows dedicado. |
| `VisualNotes.Benchmarks` | no aplica | Medición con BenchmarkDotNet, nunca como puerta del job principal. |

Use `[Trait("Category", "...")]` con los valores `Unit`, `Integration`, `Windows`, `UI`, `External` y `Slow`. Solo las clases que usan un recurso global de Windows pertenecen a `WindowsResourceCollection`; esa colección desactiva la paralelización. No desactive globalmente el paralelismo.

## Ejecución local

```bash
dotnet tool restore
dotnet restore VisualNotes.sln
dotnet test tests/VisualNotes.UnitTests
dotnet test tests/VisualNotes.ArchitectureTests
dotnet test tests/VisualNotes.IntegrationTests
dotnet test VisualNotes.sln --filter 'Category!=External&Category!=Slow&Category!=UI'
```

Las pruebas de integración aplican límites explícitos de 10–20 segundos a operaciones. Las UI usan un timeout de 60 segundos y el job tiene además un límite global para impedir pipelines bloqueados.

## Cobertura e informe

```bash
dotnet test VisualNotes.sln --filter 'Category!=UI' --settings coverage.runsettings --collect:'XPlat Code Coverage' --results-directory TestResults
reportgenerator '-reports:TestResults/**/coverage.cobertura.xml' '-targetdir:TestResults/CoverageReport' '-reporttypes:Html;Cobertura;TextSummary'
pwsh ./eng/coverage-gate.ps1 -Report TestResults/CoverageReport/Cobertura.xml -BaseRef origin/main
```

El informe contiene el sitio HTML, `Cobertura.xml` combinado y `Summary.txt`. El gate
exige 75/65 global, 90/85 en Core, 98/95 en áreas críticas y 90/85 sobre líneas nuevas
(líneas/ramas). CI publica el directorio completo como artefacto incluso si el gate
falla. Las exclusiones se limitan a código generado, migraciones EF y glue code XAML o
de composición justificado en `coverage.runsettings`; nunca se excluye lógica difícil.
No añada pruebas vacías, caminos ejecutados sin comprobar ni aserciones irrelevantes
para mover la métrica: cada test debe verificar comportamiento observable o invariantes.

## Windows y UI

Se requiere Windows 10/11 o Windows Server con .NET 8 SDK y una sesión de escritorio disponible. No ejecute UI en un agente sin escritorio. Ejecute `dotnet test tests/VisualNotes.UiTests --filter 'Category=UI'`. Los recursos compartidos de captura/ventana deben limpiarse al finalizar.

## Fixtures, SQLite y fugas

`tests/Shared/Fixtures/TestData.cs` contiene sesiones, capturas, imágenes, respuestas VLM, configuraciones y documentos deterministas. `TemporarySqliteFactory` crea una base con `Pooling=False` y nombre aleatorio por contexto. Disponga primero el contexto, llame `AssertNoOpenConnections`, disponga la fábrica y use `TestGuards.AssertNoTemporaryFiles` para detectar conexiones o archivos fugados.

## Snapshots Verify

1. Ejecute la prueba concreta. Verify escribe un archivo `.received.*` al detectar una diferencia.
2. Compare cuidadosamente `received` con `verified`: compruebe prompts completos, orden/normalización JSON, datos sensibles, rutas, GUID y fechas inestables, y toda la estructura del documento.
3. Si el cambio es intencional, copie o renombre `.received.*` a `.verified.*`; nunca acepte snapshots en bloque sin inspección.
4. Ejecute de nuevo la prueba y confirme que pasa. Incluya el `.verified.*` revisado en el commit y nunca un `.received.*`.

## Jobs independientes

En pull requests, `core-tests` ejecuta unitarias, arquitectura e integración. `ui-tests`, `mutations` y `benchmarks` son jobs separados para aislar requisitos, duración y resultados. Para benchmarks locales: `dotnet run -c Release --project tests/VisualNotes.Benchmarks`.

## Matriz manual de captura y DPI

Antes de publicar cambios de captura, repita en una sesión interactiva de Windows con escalado de **100 %, 125 %, 150 % y 200 %**:

1. Coloque un monitor a la izquierda y otro por encima del principal (orígenes negativos), y asigne escalados distintos.
2. Compruebe `FullVirtualDesktop`, `CurrentMonitor` y `ActiveWindow` contra un patrón de color conocido.
3. En `OneTimeRegion`, arrastre dentro de cada monitor y cruzando monitores; compruebe confirmar, `Enter`, cancelar, `Esc` y reiniciar (`R`).
4. Verifique píxel a píxel las dimensiones y que overlay, borde y panel de VisualNotes no estén en el PNG.
5. Compruebe que los metadatos conservan dispositivo, HWND/título, rectángulo físico, DPI, dimensiones, modo y fecha UTC.

Registre el hardware, resolución, disposición, escalados y resultado en la incidencia de entrega. Estas comprobaciones necesitan una sesión de escritorio real y no se sustituyen por el job de CI sin escritorio.

## Language-model provider tests

Provider protocol and contract tests use a simulated `HttpMessageHandler` and run in the `Unit` category. Tests that contact Gemini or OpenAI must use the `External` category, require credentials supplied outside the repository, and remain skipped by default. The pull-request workflow selects only `Unit`, `Architecture`, and `Integration`, so external tests—including pull requests from forks—cannot consume API credentials.
