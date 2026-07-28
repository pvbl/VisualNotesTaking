# Política e inventario de dependencias

`Directory.Packages.props` es el único lugar donde se declaran versiones NuGet. Los
proyectos solo declaran qué paquetes consumen. Cada cambio del grafo debe incluir los
`packages.lock.json` regenerados y revisados; CI restaura exclusivamente con
`--locked-mode` y ejecuta la auditoría transitiva de NuGet.

## Criterios para aceptar un paquete

Antes de añadir o reemplazar una dependencia, documente en el pull request:

1. la necesidad concreta y por qué BCL/código existente no la resuelve de forma más
   segura y mantenible;
2. licencia SPDX y compatibilidad con la distribución prevista (comprobadas en el
   repositorio del autor y en el `.nuspec`, no solo en un agregador);
3. propietario, repositorio oficial, fecha de la última versión y actividad reciente
   de mantenimiento/incidencias de seguridad;
4. vulnerabilidades directas y transitivas (`dotnet list VisualNotes.sln package
   --vulnerable --include-transitive`) y paquetes obsoletos;
5. impacto en tamaño, arranque, permisos, datos procesados y superficie de ataque;
6. alternativa y plan de retirada si el mantenimiento cesa.

No se admite una dependencia sin licencia identificable, abandonada o innecesaria en
captura, credenciales, persistencia, actualización o firma. Una excepción requiere
amenazas, mitigaciones, responsable y fecha de revisión explícitos. Dependency Review
bloquea licencias denegadas y vulnerabilidades moderadas o superiores; Dependabot
agrupa actualizaciones menores/parches para que sigan siendo pequeñas y revisables.
Las actualizaciones mayores permanecen separadas para exigir una revisión de migración.

## Inventario revisado

Revisión base: **2026-07-28**. Las licencias deben volver a verificarse en cada cambio;
esta tabla no sustituye la evidencia del paquete descargado ni el SBOM de cada release.

| Familia | Uso | Licencia declarada | Mantenimiento / decisión |
|---|---|---|---|
| Microsoft.Extensions, EF Core, ProtectedData y Test SDK | HTTP, persistencia, logging, DPAPI y tests | MIT | Microsoft, activas; necesarias en infraestructura. |
| OpenTelemetry | telemetría local/exportador de consola | Apache-2.0 | CNCF, activa; conservar solo mientras haya observabilidad configurada. |
| Serilog | logging y fichero local | Apache-2.0 | Activa; adaptador aislado en infraestructura. |
| DocumentFormat.OpenXml | exportación DOCX | MIT | Microsoft, activa; evita implementar el formato crítico manualmente. |
| SixLabors.ImageSharp | recorte/procesamiento de imagen | Six Labors Split License 1.0 | Activa; licencia con condiciones que deben reevaluarse antes de distribución comercial. |
| Meziantou.Analyzer y SonarAnalyzer | análisis estático | MIT / LGPL-3.0-only | Solo compilación (`PrivateAssets=all`); no se redistribuyen con la app. |
| xUnit, Verify, Shouldly, NSubstitute, FsCheck, coverlet y NetArchTest | pruebas | Apache-2.0, MIT y BSD-3-Clause según paquete | Solo desarrollo; activas al revisar esta base. |
| FlaUI | automatización UI | MIT | Solo tests; revisar compatibilidad con cada versión de UI Automation. |
| BenchmarkDotNet | benchmarks | MIT | Solo desarrollo; activa. |

Cuando una actualización cambie licencia, propietario, procedencia o transitivas, el
pull request debe tratarla como un cambio de suministro, aunque la API sea compatible.

## Releases y procedencia

El workflow de release restaura locks, audita, publica, firma todos los binarios propios
y el instalador, genera un SBOM SPDX, calcula `SHA256SUMS` y vuelve a comprobar cada hash
antes de publicar. GitHub genera además una atestación de procedencia para los
artefactos. El certificado Authenticode se recibe únicamente mediante los secretos
`WINDOWS_SIGNING_CERTIFICATE_BASE64` y `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`; nunca se
guarda en el repositorio ni en un artefacto.

El consumidor debe descargar artefactos desde la release, validar `SHA256SUMS`, comprobar
la firma Authenticode y verificar la atestación con GitHub CLI. Un hash prueba integridad,
no identidad: solo es confiable después de verificar también la procedencia/firma.
