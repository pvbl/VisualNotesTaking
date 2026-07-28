# Pruebas de VisualNotes

## Proyectos y categorías

| Proyecto | Traits `Category` | Propósito |
|---|---|---|
| `VisualNotes.UnitTests` | `Unit` | Lógica pura; se mantiene paralela. |
| `VisualNotes.IntegrationTests` | `Integration`, opcionalmente `External`/`Slow` | SQLite, sistema de archivos y adaptadores. Cada prueba obtiene una base y carpeta exclusivas. |
| `VisualNotes.ArchitectureTests` | `Architecture` | Límites entre capas. |
| `VisualNotes.UiTests` | `UI`, `Windows`, `Smoke` o `E2E` | Automatización UIA de escritorio; `Smoke` es la ruta crítica corta y `E2E` la matriz completa. |
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

## Mutaciones de Core

Ejecute `dotnet tool restore` y `dotnet stryker` desde la raíz para mutar las áreas
críticas de `VisualNotes.Core`. El score objetivo inicial es 70 % y el umbral de rotura
es 60 %; los módulos críticos subirán progresivamente a 80–85 % cuando las pruebas sean
estables. Revise el informe HTML de `StrykerOutput` y convierta los mutantes
supervivientes en pruebas de comportamiento relevantes. No ignore mutaciones ni amplíe
las exclusiones de código generado, migraciones y XAML sin una justificación documentada.

CI ejecuta esta comprobación semanalmente, bajo demanda y en pull requests que afectan
áreas críticas, y publica `StrykerOutput` como el artefacto `stryker-report`.

## Windows y UI

Se requiere Windows 10/11 o Windows Server con .NET 8 SDK y una sesión de escritorio **interactiva, desbloqueada y con resolución estable**. El runner autoalojado debe tener las etiquetas `Windows`, `X64` e `interactive-desktop`; no se admite ejecutar UIA como servicio en sesión 0. Defina `VISUALNOTES_INTERACTIVE_UI=1` únicamente después de validar esa sesión. Ejecute la ruta corta con `dotnet test tests/VisualNotes.UiTests --filter 'Category=Smoke'` y la completa con `dotnet test tests/VisualNotes.UiTests --filter 'Category=E2E|Category=Smoke'`.

Los journeys E2E localizan controles exclusivamente por `AutomationId`, salvo la prueba marcada `Capture`, que puede inspeccionar límites físicos de monitores. Usan un VLM falso, sin red y determinista. Ante una excepción, el harness guarda `desktop.png`, `uia-tree.txt`, el error y los logs JSON en `VISUALNOTES_E2E_ARTIFACTS`; CI publica el directorio solo en fallos. El workflow `desktop-e2e.yml` ejecuta smoke en cada GitHub Release marcada como pre-release (release candidate), y la suite completa de martes a sábado y bajo demanda. Los recursos compartidos de captura/ventana se limpian al finalizar.

## Fixtures, SQLite y fugas

`tests/Shared/Fixtures/TestData.cs` contiene sesiones, capturas, imágenes, respuestas VLM, configuraciones y documentos deterministas. `TemporarySqliteFactory` crea una base con `Pooling=False` y nombre aleatorio por contexto. Disponga primero el contexto, llame `AssertNoOpenConnections`, disponga la fábrica y use `TestGuards.AssertNoTemporaryFiles` para detectar conexiones o archivos fugados.

## Snapshots Verify

1. Ejecute la prueba concreta. Verify escribe un archivo `.received.*` al detectar una diferencia.
2. Compare cuidadosamente `received` con `verified`: compruebe prompts completos, orden/normalización JSON, datos sensibles, rutas, GUID y fechas inestables, y toda la estructura del documento.
3. Si el cambio es intencional, copie o renombre `.received.*` a `.verified.*`; nunca acepte snapshots en bloque sin inspección.
4. Ejecute de nuevo la prueba y confirme que pasa. Incluya el `.verified.*` revisado en el commit y nunca un `.received.*`.

## Jobs independientes

En pull requests, `core-tests` ejecuta unitarias, arquitectura e integración. `ui-tests` y `benchmarks` son jobs separados; el workflow de mutaciones se activa solo al cambiar áreas críticas. Para benchmarks locales: `dotnet run -c Release --project tests/VisualNotes.Benchmarks`.

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
