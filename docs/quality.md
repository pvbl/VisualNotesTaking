# Calidad, pruebas y métricas

Esta guía define cómo medir el proyecto de forma reproducible. Una métrica solo debe
publicarse junto al commit, sistema operativo y comando que la produjo. Los artefactos
de CI son la fuente de verdad; no se deben copiar porcentajes a mano al README.

## Puertas de calidad

| Señal | Implementación | Criterio actual |
|---|---|---|
| Formato | `dotnet format` | Sin cambios pendientes de severidad `error`. |
| Compilación | analizadores .NET, Meziantou y Sonar | Cero warnings en `Release` y CI. |
| Tests principales | xUnit: unitarios, arquitectura e integración | Todos pasan. |
| UI | xUnit en runner Windows con escritorio | Todos pasan en su job independiente. |
| Cobertura | coverlet + ReportGenerator | Se publica Cobertura/HTML; aún no hay umbral de bloqueo. |
| Mutaciones | Stryker.NET sobre algoritmos críticos | Bloquea por debajo de 60 %; 75 % bajo, 90 % alto. |
| Rendimiento | BenchmarkDotNet | Se registra como tendencia; aún no hay presupuesto automático. |
| Secretos | Gitleaks | Ningún secreto detectado. |

La ausencia de umbral de cobertura o rendimiento es deliberadamente visible: primero
se debe capturar una línea base estable y después acordar un presupuesto que no premie
tests superficiales. El mutation score complementa, pero no sustituye, la cobertura.

## Obtener métricas

### Resultado y duración de tests

```powershell
dotnet test VisualNotes.sln --configuration Release `
  --filter "Category=Unit|Category=Architecture|Category=Integration" `
  --logger "trx;LogFileName=tests.trx" --results-directory TestResults
```

El TRX contiene total, aprobadas, fallidas, omitidas y duración. Archive
`TestResults/**/tests.trx` en CI para conservar la serie histórica.

### Cobertura

```powershell
dotnet tool restore
dotnet test VisualNotes.sln --configuration Release --filter "Category!=UI&Category!=External" `
  --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet reportgenerator `
  "-reports:TestResults/**/coverage.cobertura.xml" `
  "-targetdir:TestResults/CoverageReport" `
  "-reporttypes:Html;Cobertura;TextSummary"
Get-Content TestResults/CoverageReport/Summary.txt
```

Informe al menos cobertura de líneas y ramas. Compare contra la rama base y revise las
líneas sin cubrir de código nuevo; un porcentaje global aislado no demuestra calidad.

### Mutation testing

```powershell
dotnet tool restore
dotnet stryker --test-project tests/VisualNotes.UnitTests/VisualNotes.UnitTests.csproj
```

Stryker muta actualmente normalización de bounding boxes, recorte sintético y
deduplicación. Revise mutantes supervivientes: pueden revelar una aserción débil, código
equivalente o funcionalidad no probada. El informe queda en `StrykerOutput`.

### Rendimiento y memoria

```powershell
dotnet run --configuration Release --project tests/VisualNotes.Benchmarks -- --filter "*"
```

BenchmarkDotNet mide tiempo y asignaciones para composición documental, almacenamiento
de capturas 1080p/1440p/4K, filtrado de 1.000 capturas y exportación DOCX grande. Compare
en hardware y configuración equivalentes; no trate resultados de máquinas distintas
como una regresión concluyente.

## Línea base

No hay métricas numéricas verificadas en este checkout porque deben producirse con el
.NET 8 SDK y, para la solución completa, Windows. Para establecer la primera línea base:

1. Ejecute los cuatro bloques anteriores en el mismo commit de un runner Windows.
2. Adjunte TRX, Cobertura/HTML, Stryker y BenchmarkDotNet como artefactos.
3. Registre fecha, SHA, versión del SDK, runner y filtros.
4. Observe varios runs antes de fijar umbrales de cobertura o rendimiento.
5. Cuando la señal sea estable, eleve los umbrales gradualmente y documente el motivo.

## Interpretación responsable

- **Tests:** un total alto no implica escenarios relevantes; revise categorías y
  casos límite.
- **Cobertura:** mida línea y rama, y no excluya código difícil sin justificación.
- **Mutaciones:** investigue supervivientes en vez de perseguir el 100 % ciegamente.
- **Rendimiento:** use mediana/distribución y asignaciones, no una única ejecución.
- **Calidad estática:** cero warnings no reemplaza revisión de diseño, seguridad y UX.

## CI

`.github/workflows/quality.yml` separa escaneo de secretos, tests principales, UI,
mutaciones y benchmarks. Esto evita que requisitos de escritorio o cargas lentas
oculten el resultado principal. Los trabajos tienen timeout y la cobertura se publica
como artefacto. Toda modificación de estas puertas debe actualizar este documento y
`tests/README.md` en el mismo pull request.
