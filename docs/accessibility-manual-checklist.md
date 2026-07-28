# Checklist manual de accesibilidad

Ejecutar en Windows con escalado **100 %, 150 % y 200 %**, tema de contraste alto y Narrador (o NVDA). Registrar versión, monitor, escalado y resultado. No aprobar un flujo solo por las pruebas automatizadas.

## Criterios comunes

- [ ] Recorrer todos los controles con `Tab`/`Mayús+Tab`: el orden coincide con el visual, no hay trampas y el foco tiene un contorno visible.
- [ ] Activar botones con `Espacio`/`Enter`, listas con flechas y cancelar con `Escape`, sin usar ratón.
- [ ] Narrador anuncia nombre, rol, estado, descripción, valor y cambios dinámicos; no anuncia controles decorativos.
- [ ] Texto y controles no se cortan ni solapan con escalado 200 %; el texto normal mantiene al menos 14 px efectivos.
- [ ] En tema normal y contraste alto, texto y controles alcanzan 4,5:1 (3:1 para texto grande y límites/indicadores). Verificar con Accessibility Insights.
- [ ] Error, selección, pausa, bloqueo, importancia y revisión se distinguen mediante texto o icono accesible, no solo mediante color.

## Selección de región

- [ ] Al abrir, el foco entra en el selector y Narrador anuncia las instrucciones.
- [ ] Dibujar con ratón y confirmar con `Enter`; cancelar con `Escape`; reiniciar con `R`.
- [ ] Mover con flechas, redimensionar con `Mayús+flechas` y eliminar con `Supr`.
- [ ] Alternar bloqueo con `L` y visibilidad con `H`; Narrador anuncia tamaño y estados «Bloqueada/Editable» y «Borde oculto/visible».
- [ ] Repetir cruzando monitores con orígenes negativos y DPI distintos; el borde y las coordenadas siguen la región.

## Panel flotante

- [ ] `Tab` recorre modo mínimo, modo de captura, capturar, pausar, navegación de sección, deshacer, importante, contexto y opacidad, y vuelve al inicio.
- [ ] Probar `Ctrl+C`, `Espacio`, `Ctrl+Z` y `Ctrl+M`; cada acción coincide con el nombre/descripción anunciados.
- [ ] En modo mínimo sigue disponible un control enfocable para restaurar el panel; al expandir no se pierde el foco.
- [ ] Cambios de sesión, sección, cola y número de capturas se anuncian sin interrumpir de forma agresiva.
- [ ] Con 200 % de escalado se puede redimensionar y alcanzar cada control sin solapamientos.

## Edición de cajas y capturas

- [ ] Seleccionar una o varias capturas solo con teclado y verificar que Narrador anuncia cantidad y estado.
- [ ] Editar contexto, instrucción y etiquetas; los campos se anuncian por su propósito, no únicamente como «edición».
- [ ] Ejecutar excluir, eliminar, restaurar, mover, reprocesar y deshacer; el resultado incluye texto/estado además de color.
- [ ] Para una captura fallida o que requiere revisión, Narrador anuncia «Requiere atención» y el motivo permanece visible en contraste alto.
- [ ] Revisar las cajas delimitadoras de la imagen: seleccionar, mover, redimensionar, bloquear, ocultar y eliminar por teclado; confirmar foco visible y anuncio de geometría/estado.
