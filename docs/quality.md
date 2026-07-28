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
| Cobertura global | coverlet + ReportGenerator | Bloquea por debajo de 75 % de líneas o 65 % de ramas. |
| Cobertura de Core | `eng/coverage-gate.ps1` | Objetivo reforzado: 90 % de líneas y 85 % de ramas. |
| Código crítico | `eng/coverage-gate.ps1` | 98 % de líneas y 95 % de ramas. |
| Código nuevo | diff contra la rama base | 90 % de líneas y 85 % de ramas modificadas. |
| Mutaciones | Stryker.NET sobre comportamiento crítico de Core | Objetivo mínimo 70 %; bloquea por debajo de 60 %. |
| Rendimiento | BenchmarkDotNet | Se registra como tendencia; aún no hay presupuesto automático. |
| Secretos | Gitleaks | Ningún secreto detectado. |

Los umbrales globales son el suelo inicial, no el destino. Core se mantiene en el
objetivo progresivo 90/85 y el código nuevo no puede esconderse detrás de cobertura
histórica. El mutation score complementa, pero no sustituye, la cobertura.

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
  --settings coverage.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet reportgenerator `
  "-reports:TestResults/**/coverage.cobertura.xml" `
  "-targetdir:TestResults/CoverageReport" `
  "-reporttypes:Html;Cobertura;TextSummary"
Get-Content TestResults/CoverageReport/Summary.txt
./eng/coverage-gate.ps1 -Report TestResults/CoverageReport/Cobertura.xml -BaseRef origin/main
```

El gate calcula líneas y ramas globales, de Core, críticas y nuevas. En un pull request
usa el ancestro común con la rama base; en un push usa el commit anterior. Si un cambio
no contiene líneas instrumentables, el gate de código nuevo se declara no aplicable.

Se consideran críticos la normalización de cajas, la resolución/modelo de
configuración, las reglas de caché/deduplicación, las transiciones de la cola durable y
las reglas de privacidad de respuestas, configuración portable y diagnósticos. Sus
patrones están enumerados explícitamente en `eng/coverage-gate.ps1` para que una nueva
área crítica requiera una decisión revisable, no una exclusión implícita.

`coverage.runsettings` excluye solamente artefactos generados por compilador, las
migraciones generadas por EF y glue code sin decisiones de dominio: code-behind XAML y
raíces de composición/registro. Adaptadores, persistencia y lógica difícil permanecen
instrumentados. Toda nueva exclusión debe justificar qué genera el archivo o por qué
es exclusivamente ensamblado de dependencias.

### Mutation testing

```powershell
dotnet tool restore
dotnet stryker
```

Stryker muta inicialmente el comportamiento crítico de `VisualNotes.Core`: cajas,
recorte y escalado; resolución de configuración; deduplicación, caché y cola; privacidad
de respuestas; y composición de prompts y documentos. Se excluyen explícitamente código
generado, migraciones y vistas/code-behind XAML. Aunque hoy esas exclusiones no forman
parte de Core, quedan declaradas para evitar ampliar accidentalmente el alcance si cambia
la estructura del proyecto. No añada una exclusión ni marque una mutación como ignorada
sin registrar en la revisión la razón técnica y, cuando corresponda, por qué el mutante
es equivalente.

El objetivo mínimo inicial es 70 % y el build falla por debajo de 60 %. El rango alto se
fija en 85 % para hacer visible el objetivo al que deben elevarse gradualmente los módulos
críticos (80–85 %) cuando la suite madure; no se debe subir el gate hasta que la señal sea
estable. Revise cada mutante superviviente y añada pruebas que observen comportamiento
relevante, límites y efectos, no aserciones creadas únicamente para matar una mutación.
El informe HTML queda en `StrykerOutput`.

### Rendimiento y memoria

```powershell
dotnet run --configuration Release --project tests/VisualNotes.Benchmarks -- --filter "*"
```

BenchmarkDotNet mide tiempo y asignaciones para composición documental, almacenamiento
de capturas 1080p/1440p/4K, filtrado de 1.000 capturas y exportación DOCX grande. Compare
en hardware y configuración equivalentes; no trate resultados de máquinas distintas
como una regresión concluyente.

## Evolución de los objetivos

El suelo global inicial es 75/65. Core ya expresa el siguiente objetivo, 90/85, y las
áreas críticas se acercan al 100 % con 98/95. Los umbrales solo pueden mantenerse o
subir cuando la señal sea estable; bajarlos exige una decisión explícita y documentada.
El informe HTML, Cobertura combinado y resumen textual se publican juntos como el
artefacto `coverage` incluso si falla el gate, para permitir diagnosticar el resultado.

## Interpretación responsable

- **Tests:** un total alto no implica escenarios relevantes; revise categorías y
  casos límite.
- **Cobertura:** mida línea y rama, y no excluya código difícil sin justificación.
- **Código nuevo:** revise el diff además del total; no acepte aserciones irrelevantes,
  tests vacíos ni ejecución sin comprobar resultados para satisfacer el porcentaje.
- **Mutaciones:** investigue supervivientes en vez de perseguir el 100 % ciegamente.
- **Rendimiento:** use mediana/distribución y asignaciones, no una única ejecución.
- **Calidad estática:** cero warnings no reemplaza revisión de diseño, seguridad y UX.

## CI

`.github/workflows/quality.yml` separa escaneo de secretos, tests principales, UI y
benchmarks. Las mutaciones viven en `.github/workflows/mutations.yml`: se ejecutan cada
lunes, bajo demanda y en pull requests que cambien módulos críticos, sus tests o la
propia configuración. El workflow publica siempre `StrykerOutput` (incluido el informe
HTML) como artefacto para poder revisar supervivientes incluso cuando falla el umbral.
Esto evita que cargas lentas oculten el resultado principal. Los trabajos tienen timeout
y la cobertura se publica como artefacto. Toda modificación de estas puertas debe
actualizar este documento y `tests/README.md` en el mismo pull request.
