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

- [ ] En el panel, «Región sin definir», «Región lista» y «Región bloqueada» se anuncian como estados distintos.
- [ ] **Definir región** abre el selector; tras confirmar cambia a **Redefinir región** y habilita **Bloquear**.
- [ ] Una región bloqueada no se puede redefinir hasta activar **Desbloquear**; el mensaje explica la recuperación.
- [ ] Al abrir, el foco entra en el selector y Narrador anuncia las instrucciones.
- [ ] Dibujar con ratón y confirmar con `Enter`; cancelar con `Escape`; reiniciar con `R`.
- [ ] Mover con flechas, redimensionar con `Mayús+flechas` y eliminar con `Supr`.
- [ ] Alternar bloqueo con `L` y visibilidad con `H`; Narrador anuncia tamaño y estados «Bloqueada/Editable» y «Borde oculto/visible».
- [ ] Repetir cruzando monitores con orígenes negativos y DPI distintos; el borde y las coordenadas siguen la región.

## Selección de ventana

- [ ] Al elegir **Ventana** y capturar, el foco entra en la lista «Elegir ventana».
- [ ] Recorrer la lista con flechas, confirmar con `Enter` o **Capturar esta ventana** y cancelar con `Escape`.
- [ ] Narrador anuncia el título de cada ventana; las ventanas de VisualNotes no aparecen como destinos.
- [ ] Si no hay ventanas capturables, el mensaje «Sin ventanas» explica la causa y devuelve el foco al panel.

## Panel flotante

- [ ] `Tab` recorre sesión, curso, módulo, sección del curso, creación de sección, campos del próximo elemento, captura, posición, modo de captura, región, pausa, navegación, deshacer, importancia, contexto y opacidad, y vuelve al inicio.
- [ ] Curso y módulo aceptan texto nuevo sin impedir seleccionar valores existentes; el nombre largo no oculta el propósito del control al 200 %.
- [ ] Cambiar la sección activa actualiza la ruta `Curso › Módulo › Sección` y Narrador anuncia el destino.
- [ ] Probar `Ctrl+C`, `Espacio`, `Ctrl+Z` y `Ctrl+M`; cada acción coincide con el nombre/descripción anunciados.
- [ ] En modo mínimo sigue disponible un control enfocable para restaurar el panel; al expandir no se pierde el foco.
- [ ] Cambios de sesión, sección, cola y número de capturas se anuncian sin interrumpir de forma agresiva.
- [ ] Con 200 % de escalado se puede redimensionar y alcanzar cada control sin solapamientos.

## Edición de cajas y capturas

- [ ] Abrir **Capturas** con una sesión vacía, capturas antiguas y capturas nuevas; la vista no produce un error de enlace.
- [ ] Seleccionar una o varias capturas solo con teclado y verificar que Narrador anuncia cantidad y estado.
- [ ] La ruta académica completa permite distinguir secciones homónimas de cursos o módulos diferentes.
- [ ] Editar contexto, instrucción y etiquetas; los campos se anuncian por su propósito, no únicamente como «edición».
- [ ] Ejecutar excluir, eliminar, restaurar, mover, reprocesar y deshacer; el resultado incluye texto/estado además de color.
- [ ] Para una captura fallida o que requiere revisión, Narrador anuncia «Requiere atención» y el motivo permanece visible en contraste alto.
- [ ] Revisar las cajas delimitadoras de la imagen: seleccionar, mover, redimensionar, bloquear, ocultar y eliminar por teclado; confirmar foco visible y anuncio de geometría/estado.
