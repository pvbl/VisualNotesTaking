# VisualNotes

VisualNotes es una aplicación de escritorio para Windows que convierte capturas de
pantalla en apuntes estructurados. Separa las sesiones de trabajo de la organización
académica por curso, módulo y sección, permite capturar una región, una ventana, un
monitor o todo el escritorio y ofrece una vista previa antes de exportar el resultado
a Word (`.docx`).

> [!IMPORTANT]
> El proyecto sigue en desarrollo: todavía no hay una versión estable ni binarios
> firmados para uso general. No utilices capturas o credenciales sensibles en un
> entorno que no controles.

## Qué incluye

- Captura del escritorio virtual, monitor actual, ventana elegida o región persistente.
- Sesiones de trabajo que pueden reunir secciones de varios cursos y módulos.
- Jerarquía académica `Curso > Módulo > Sección del curso`.
- Clasificación, edición, deduplicación y reprocesado de capturas.
- Adaptadores para análisis visual con OpenAI y Google Gemini.
- Vista previa semántica y exportación Open XML (`.docx`).
- Persistencia local con SQLite, copias de seguridad y diagnósticos estructurados.
- Atajos globales, icono en la bandeja y soporte para varios monitores y DPI.

## Requisitos

| Requisito | Obligatorio | Notas |
|---|---:|---|
| Windows 10 2004 (build 19041) o posterior | Sí | Windows 11 también es compatible. La aplicación usa WPF y APIs de escritorio de Windows. |
| Equipo x64 | Sí | Es la arquitectura de publicación e instalador configurada actualmente. |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Sí, al compilar | Comprueba la instalación con `dotnet --version`; debe devolver `8.x`. No basta con instalar solo el runtime. |
| Git | Sí, al clonar | También puedes descargar el código fuente como ZIP. |
| Visual Studio 2022 | No | Si lo usas, instala la carga **Desarrollo de escritorio de .NET**. |
| Clave de OpenAI o Gemini | No | Las capturas y la gestión local funcionan sin clave; se necesita para llamadas reales al proveedor. |

> [!NOTE]
> La compilación y ejecución de la interfaz requieren Windows. Aunque .NET permita
> restaurar parte de la solución en otros sistemas, la aplicación no es
> multiplataforma.

## Instalación desde el código fuente

### 1. Clonar y entrar en el repositorio

Sustituye la URL por la de tu fork o la ubicación desde la que obtuviste el proyecto:

```powershell
git clone <URL-DEL-REPOSITORIO>
cd VisualNotesTaking
```

### 2. Restaurar y compilar

Ejecuta estos comandos desde la raíz del repositorio:

```powershell
dotnet restore VisualNotes.sln
dotnet build VisualNotes.sln --configuration Release --no-restore
```

Si solo quieres arrancar la aplicación durante el desarrollo, también puedes
compilar en `Debug`. Las herramientas locales para cobertura y mutation testing no
son necesarias para ejecutar VisualNotes; los contribuidores pueden instalarlas con
`dotnet tool restore`.

### 3. Iniciar VisualNotes

```powershell
dotnet run --project src/VisualNotes.App/VisualNotes.App.csproj --configuration Release --no-build
```

Si no ejecutaste antes el `build`, elimina `--no-build` del comando. También puedes
abrir `VisualNotes.sln` en Visual Studio, seleccionar `VisualNotes.App` como proyecto
de inicio y pulsar **F5**.

### 4. Completar el primer inicio

El asistente inicial solicita:

1. **Carpeta de almacenamiento.** Ahí se guardan la base SQLite, capturas y
   diagnósticos. La ubicación predeterminada es `%LOCALAPPDATA%\VisualNotes`.
2. **Proveedor de análisis.** Selecciona **Ninguno**, **OpenAI** o **Gemini**.
3. **Consentimiento de privacidad.** Actívalo solo si autorizas el envío de capturas
   al proveedor elegido.
4. **Atajos globales.** Se pueden dejar habilitados o configurar después.
5. **Captura de prueba.** Comprueba que Windows permite capturar la pantalla.

Para una ejecución aislada (por ejemplo, durante desarrollo) puedes indicar otra
carpeta antes de abrir la aplicación:

```powershell
$env:VISUALNOTES_DATA_DIRECTORY = "$env:TEMP\VisualNotes-dev"
dotnet run --project src/VisualNotes.App/VisualNotes.App.csproj
```

La variable solo cambia la carpeta de datos para ese proceso o terminal. No pongas
claves API en esta variable ni en archivos del repositorio.

### Logs y depuración

Al ejecutar VisualNotes desde PowerShell o una terminal, la aplicación muestra logs
breves de estado, advertencias y errores. También conserva logs estructurados JSON en
`<carpeta de datos>\diagnostics`, con rotación diaria y un máximo de 14 archivos. Los
dos destinos aplican el mismo filtrado de secretos y contenido sensible.

El nivel predeterminado es `Information`. Para investigar un problema de desarrollo,
activa temporalmente `Debug` antes de arrancar la aplicación:

```powershell
$env:VISUALNOTES_LOG_LEVEL = "Debug"
dotnet run --project src/VisualNotes.App/VisualNotes.App.csproj
```

Los valores admitidos son `Verbose`, `Debug`, `Information`, `Warning`, `Error` y
`Fatal` (sin distinguir mayúsculas). Un valor desconocido genera una advertencia y
usa `Information`. No registres prompts, claves, imágenes ni contenido de apuntes;
los logs deben contener únicamente estado operativo e identificadores técnicos.

## Configurar una API

### Obtener una clave

Utiliza el panel oficial del proveedor y revisa sus condiciones, disponibilidad y
facturación antes de crearla:

- **OpenAI:** [crear y administrar claves de API](https://platform.openai.com/api-keys).
- **Google Gemini:** [obtener una clave en Google AI Studio](https://aistudio.google.com/app/apikey).

Una suscripción a una aplicación de chat no implica necesariamente crédito para su
API. La cuenta del proveedor debe tener acceso al modelo configurado y, cuando
corresponda, facturación activa. Nunca pegues una clave en `README.md`, archivos
`.json`, capturas, incidencias o commits.

### Guardar la clave en VisualNotes

1. Abre VisualNotes y entra en **Configuración**.
2. En **Configuración por niveles**, selecciona el proveedor y modelo deseados. El
   valor predeterminado visible actualmente es OpenAI con `gpt-4.1-mini`.
3. En **Credenciales de API**, pega la clave en **Nueva credencial**.
4. Pulsa **Guardar**. La interfaz vacía el campo y muestra la credencial enmascarada.
5. Configura los dos perfiles según el flujo que vayas a utilizar:
   - **Extracción:** análisis de la imagen y obtención de contenido estructurado.
   - **Composición:** generación o reorganización del documento final.
   Puedes usar la misma clave en ambos perfiles si la cuenta tiene los permisos
   necesarios, o claves distintas para separar acceso y consumo.
6. Pulsa **Eliminar** cuando quieras revocar la copia local y revoca también la clave
   desde el panel del proveedor si pudo quedar expuesta.

> [!WARNING]
> **Verificar** comprueba que el texto introducido coincide con la copia guardada;
> actualmente no realiza una petición al proveedor ni valida saldo, permisos o el
> nombre del modelo. Además, los adaptadores OpenAI/Gemini y la pantalla de
> credenciales están implementados, pero el flujo principal todavía no conecta esas
> credenciales con un análisis remoto completo. Esta limitación es propia del estado
> de desarrollo actual, no un error de tu clave.

### Cómo se protegen las credenciales

- Se cifran mediante Windows DPAPI para el usuario actual.
- Se almacenan fuera de la carpeta de sesiones, en
  `%LOCALAPPDATA%\VisualNotesCredentials`.
- No se incluyen en las exportaciones portables ni en las copias de seguridad.
- No se pueden trasladar copiando el archivo cifrado a otro usuario o equipo.
- La aplicación nunca vuelve a mostrar el valor: solo indica que existe mediante una
  máscara.

Para rotar una clave, crea una nueva en el proveedor, guárdala en el perfil
correspondiente, comprueba tu flujo y después revoca la anterior. No edites a mano
los archivos `.credential`.

## Cómo utilizar VisualNotes

### Flujo básico

1. **Crea o recupera una sesión.** Una sesión representa el periodo durante el que
   estás tomando apuntes y puede abarcar varios cursos. En **Sesión**, indica un
   nombre y un curso y módulo iniciales; pulsa **Crear desde cero**. Para retomar
   trabajo anterior, selecciónalo en **Sesiones recientes** y pulsa **Continuar**.
2. **Elige el destino académico.** En el panel de captura selecciona o escribe el
   curso y el módulo, y elige una sección existente o crea otra. Cada captura y
   apunte se guarda en la sección activa. Puedes cambiar de curso o módulo sin cerrar
   la sesión.
3. **Elige qué capturar.** Utiliza el panel flotante, que permanece por encima de
   otras ventanas, selecciona un modo y pulsa **Capturar**.
4. **Pausa cuando sea necesario.** El botón **Pausar / reanudar** detiene o recupera
   el estado de la sesión. El nombre, sección y estado actuales aparecen tanto en el
   panel como en la barra lateral.
5. **Revisa el resultado.** Abre **Capturas** para filtrar y seleccionar elementos,
   añadir contexto, instrucciones o etiquetas, moverlos de sección, excluirlos,
   eliminarlos o solicitar su reprocesado.
6. **Prepara la salida.** En **Documento**, selecciona qué elementos incluir,
   reordénalos, elige el alcance y pulsa **Exportar**.

Los cambios de sesión se guardan en la base de datos local. Cerrar la ventana
principal no termina necesariamente el proceso: VisualNotes continúa en la bandeja
para que puedas seguir capturando. Utiliza el menú del icono para volver a abrirla o
salir.

### Modelo de organización

VisualNotes distingue dos estructuras que cumplen funciones diferentes:

```text
Sesión de trabajo
├─ captura o apunte → Curso A › Módulo 1 › Sección X
├─ captura o apunte → Curso A › Módulo 2 › Sección Y
└─ captura o apunte → Curso B › Módulo 1 › Sección Z
```

- **Sesión:** periodo continuo de toma de notas. Conserva pausa, reanudación, fecha,
  documento previsto y el conjunto de capturas.
- **Curso:** agrupación académica principal.
- **Módulo:** parte de un curso.
- **Sección del curso:** destino activo de las capturas y apuntes. Es la unidad que
  se ordena y compone en el documento.

El curso y módulo indicados al crear una sesión son sólo el destino inicial. No
limitan el contenido posterior de esa sesión.

### Modos de captura

| Modo del panel | Qué captura | Comportamiento |
|---|---|---|
| **Pantalla** | Un monitor completo | Es el modo predeterminado y captura el monitor donde se encuentra el cursor. |
| **Región** | Una parte rectangular de la pantalla | Si existe una región guardada, la reutiliza. Si no existe, abre el selector y recuerda la nueva región. |
| **Ventana** | Una ventana visible elegida por el usuario | Abre una lista de ventanas capturables y excluye las ventanas de VisualNotes para evitar autocapturas. |
| **Todos los monitores** | Todo el escritorio virtual | Incluye el área combinada de todas las pantallas conectadas. |

El panel muestra si la región está **sin definir**, **lista** o **bloqueada**. Usa
**Definir región** o **Redefinir región** desde el propio panel; también puedes usar
**Ctrl+Mayús+R**. Una región bloqueada debe desbloquearse antes de redefinirla. Durante
la selección:

- arrastra para marcar el rectángulo;
- pulsa **Intro** para confirmar o **Esc** para cancelar;
- usa las flechas para moverlo y **Mayús + flecha** para cambiar su tamaño;
- pulsa **R** para reiniciar, **L** para bloquear, **H** para ocultar o **Supr** para
  eliminar la selección.

Una vez definida, **Ctrl+Mayús+C** captura esa región. También puedes hacerlo desde
el menú del icono de la bandeja. **Bloquear** evita cambios accidentales, pero no
impide seguir capturando la región guardada.

### Panel flotante y atajos

El panel flotante permite trabajar sin regresar a la ventana principal:

- **Contexto de los apuntes:** cambia la sesión de trabajo y el destino
  `Curso › Módulo › Sección` sin interrumpir la captura.
- **Ctrl+C:** capturar con el modo seleccionado cuando el panel tiene el foco.
- **Espacio:** pausar o reanudar la sesión cuando el panel tiene el foco.
- **Ctrl+M:** alternar el modo mínimo del panel.
- **Ctrl+Z:** deshacer la última acción compatible.
- **Sección anterior/siguiente:** cambia la sección activa sin detener la sesión.
- **Importante** y **Contexto:** actúan sobre la captura correspondiente cuando el
  flujo de capturas está conectado.

En **Configuración → Atajos globales** puedes activar o modificar los atajos que
funcionan aunque VisualNotes no tenga el foco. Los valores de tecla se muestran como
códigos de tecla virtual de Windows. Si una combinación ya está registrada por otra
aplicación, VisualNotes muestra el conflicto al guardar; elige otra combinación.

### Revisar capturas y preparar el documento

En **Capturas** puedes usar varios filtros a la vez: sección, etiqueta, estado,
importancia y revisión. La selección múltiple permite excluir, eliminar, restaurar,
reprocesar o mover varios elementos de una sola vez. En el panel de detalle puedes:

- añadir información que ayude a interpretar la imagen;
- escribir una instrucción específica de procesamiento;
- aplicar etiquetas rápidas o escribir etiquetas propias;
- volver a analizar la captura o regenerar su nota.

Cada fila muestra la ruta académica completa
`Curso › Módulo › Sección`, por lo que dos secciones con el mismo título siguen
siendo distinguibles. La pestaña **Markdown previo** es de sólo lectura; **Resultado**
sí permite editar y guardar el Markdown final.

En **Documento**, revisa cualquier aviso de contenido pendiente o archivos ausentes.
Selecciona una fila para incluirla, excluirla o cambiar su posición. El alcance de
exportación determina si se prepara todo el documento o solo una parte.

Las capturas y los apuntes de texto se incorporan al repositorio local de la sesión
en cuanto se crean. Si una imagen no puede escribirse o registrarse, VisualNotes
muestra el detalle recuperable y deja constancia en los diagnósticos.

## Datos locales y reinicio de la configuración

| Contenido | Ubicación predeterminada |
|---|---|
| Configuración del primer inicio | `%LOCALAPPDATA%\VisualNotes\bootstrap.json` |
| Base de datos de sesiones | `%LOCALAPPDATA%\VisualNotes\visualnotes.db` |
| Diagnósticos | `%LOCALAPPDATA%\VisualNotes\diagnostics` |
| Credenciales cifradas | `%LOCALAPPDATA%\VisualNotesCredentials` |

La carpeta de datos puede ser distinta si la elegiste en el asistente o definiste
`VISUALNOTES_DATA_DIRECTORY`. Actualizar, reparar o desinstalar la aplicación conserva
deliberadamente sesiones, configuración y credenciales.

Al abrir una versión nueva, VisualNotes aplica las migraciones de SQLite antes de
mostrar la interfaz. En la migración a la jerarquía académica por sección, cada
sección antigua hereda automáticamente el curso y módulo que tenía su sesión. Los
campos históricos se conservan internamente para compatibilidad; no es necesario
recrear sesiones ni capturas.

Antes de borrar datos, cierra VisualNotes y guarda una copia si la necesitas. Para
repetir únicamente el asistente inicial, elimina `bootstrap.json`; esto no elimina la
base de datos ni las credenciales. Para retirar una clave, utiliza preferentemente el
botón **Eliminar** y revócala también en el proveedor.

## Solución de problemas

### `dotnet` no se reconoce o aparece una versión incorrecta

Instala el **SDK de .NET 8**, abre una terminal nueva y ejecuta:

```powershell
dotnet --info
```

### La restauración de paquetes falla

Comprueba la conexión y el acceso a NuGet, y vuelve a restaurar desde la raíz:

```powershell
dotnet nuget locals all --clear
dotnet restore VisualNotes.sln
```

Si el problema continúa, revisa que ningún proxy o cortafuegos esté bloqueando
`https://api.nuget.org` y conserva el mensaje completo para diagnosticarlo.

### No aparece la ventana al volver a abrir

VisualNotes puede seguir ejecutándose en la bandeja del sistema. Usa el icono de
VisualNotes y selecciona **Abrir VisualNotes** antes de iniciar otra instancia.

### La captura no funciona

- Ejecuta la captura de prueba del asistente.
- Comprueba que la sesión de Windows esté desbloqueada.
- En **Región**, usa **Definir región** antes de bloquearla. Si ya está bloqueada,
  pulsa **Desbloquear** antes de redefinirla.
- En **Ventana**, comprueba que la ventana de destino esté visible y tenga título; las
  ventanas de VisualNotes se omiten deliberadamente.
- Prueba primero con una pantalla local; Escritorio remoto, máquinas virtuales y
  software de protección pueden limitar la captura.
- Revisa los archivos JSON de `diagnostics`, eliminando información sensible antes
  de compartirlos.

### La pestaña Capturas está vacía

Comprueba que estás en la sesión correcta y que tiene una sección activa. Las
capturas nuevas se asignan a esa sección. Usa **Limpiar filtros** y desactiva
**Papelera** para volver a la cronología normal. Las capturas de otras sesiones no se
mezclan en la vista actual.

### La clave está guardada pero el análisis no funciona

Confirma el proveedor y modelo configurados, el consentimiento de privacidad, el
acceso del proyecto del proveedor y su facturación. Ten en cuenta también la
limitación de integración remota descrita en [Configurar una API](#configurar-una-api):
el botón **Verificar** no prueba una llamada real.

## Verificación para desarrollo

```powershell
# Formato y analizadores
dotnet format VisualNotes.sln --verify-no-changes --severity error
dotnet build VisualNotes.sln --configuration Release /warnaserror

# Suite que no depende de UI ni de proveedores externos
dotnet test VisualNotes.sln --configuration Release `
  --filter "Category=Unit|Category=Architecture|Category=Integration"
```

Las pruebas de UI requieren Windows con una sesión de escritorio interactiva y
desbloqueada. Consulta la [guía detallada de pruebas](tests/README.md) para conocer
los filtros, snapshots, fixtures y la matriz manual de captura.

## Estructura del repositorio

```text
src/
  VisualNotes.App/              WPF, vistas, view models y adaptadores Windows
  VisualNotes.Core/             dominio y casos de uso sin dependencias de UI
  VisualNotes.Infrastructure/   SQLite, archivos, proveedores, exportación y diagnóstico
tests/
  VisualNotes.UnitTests/        lógica y contratos aislados
  VisualNotes.IntegrationTests/ persistencia, archivos y exportación
  VisualNotes.ArchitectureTests/ límites entre capas
  VisualNotes.UiTests/          automatización de escritorio Windows
  VisualNotes.Benchmarks/       escenarios BenchmarkDotNet
```

## Documentación

- [Arquitectura y flujo de datos](docs/architecture.md)
- [Calidad, pruebas y métricas](docs/quality.md)
- [Dependencias, licencias y cadena de suministro](docs/dependencies.md)
- [Guía detallada de pruebas](tests/README.md)
- [Cómo contribuir](CONTRIBUTING.md)

## Seguridad y privacidad

- No incluyas claves, capturas reales ni datos personales en commits o fixtures.
- Revisa los paquetes de diagnóstico antes de compartirlos.
- Las llamadas a modelos pueden enviar imágenes y texto al proveedor configurado;
  usa únicamente contenido cuya transferencia esté autorizada.
- Informa vulnerabilidades por un canal privado al equipo mantenedor, no mediante una
  incidencia pública.

## Licencia

El repositorio no contiene todavía un archivo de licencia. Hasta que se añada uno,
no debe asumirse permiso para redistribuir o reutilizar el código fuera de los
términos acordados con sus propietarios.
