# Contribuir a VisualNotes

## Flujo recomendado

1. Cree una rama pequeña y enfocada.
2. Añada o actualice pruebas para el comportamiento modificado.
3. No mezcle refactors amplios con cambios funcionales.
4. Ejecute las comprobaciones indicadas abajo.
5. Explique en el pull request el problema, la solución, los riesgos y la evidencia.

## Comprobaciones antes del pull request

En PowerShell, desde la raíz:

```powershell
dotnet tool restore
dotnet restore VisualNotes.sln --locked-mode
dotnet format VisualNotes.sln --verify-no-changes --no-restore
dotnet build VisualNotes.sln --configuration Debug --no-restore /warnaserror
dotnet build VisualNotes.sln --configuration Release --no-restore /warnaserror
dotnet test VisualNotes.sln --configuration Release --no-build `
  --filter "Category=Unit|Category=Architecture|Category=Integration"
```

Para cambios de interfaz o captura, ejecute además las pruebas UI en una sesión de
escritorio y siga la matriz manual de `tests/README.md`. Para cambios de rutas críticas
de Core, compruebe mutation testing según `docs/quality.md`.

## Convenciones

- Respete `.editorconfig`, C# 12, nullable reference types y los analizadores.
- Mantenga `Core` independiente de WPF, almacenamiento y proveedores concretos.
- Prefiera pruebas deterministas, nombres que describan el resultado y un único
  motivo de fallo por prueba.
- Marque las pruebas con `Unit`, `Integration`, `Architecture`, `UI`, `Windows`,
  `External` o `Slow`, según corresponda.
- No desactive reglas globalmente para ocultar un problema. Una supresión puntual
  debe incluir justificación y el alcance mínimo.
- Nunca acepte automáticamente archivos Verify `.received.*`; revise el diff y
  actualice solo el `.verified.*` intencional.

## Definición de terminado

- Compila en `Release` sin warnings.
- Pasan las pruebas aplicables y se documentan las limitaciones no verificadas.
- No se introducen secretos ni datos personales.
- La documentación pública refleja cambios de uso, arquitectura u operación.
- El pull request incluye evidencia reproducible, no solo afirmaciones de calidad.
