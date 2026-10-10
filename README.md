# QAFlow Recorder 1.0

QAFlow Recorder es una aplicación de Windows para registrar evidencia de pruebas de software y, opcionalmente, trabajar con un entorno híbrido de automatización web. Está diseñada para que el QA pruebe de forma natural mientras la aplicación registra interacciones, evidencias visuales, anotaciones, defectos y contexto de ejecución.

[![Descargar Última Versión](https://img.shields.io/badge/Descargar-QA_Flow_Recorder_v1.0-blue?style=for-the-badge&logo=github)](https://github.com/lcalero85/QAFlow-Recorder/releases/latest)

> 🚀 **Descarga rápida:** [Haz clic aquí para descargar el instalador (.exe)](https://github.com/lcalero85/QAFlow-Recorder/releases/latest)

## Modos del producto

### Recorder Only

Es el modo nativo de QAFlow Recorder. Puede documentar interacciones realizadas sobre aplicaciones visibles de Windows, incluyendo navegadores y software de escritorio. Incluye capturas, anotaciones, bugs, revisión de sesión, línea de tiempo de evidencias, carpetas y etiquetas, zonas de privacidad, checkpoints, baselines visuales, cobertura, análisis de riesgo, insights y reportes PDF.

### Hybrid Testing · Web Automation

La automatización es opcional y puede desactivarse desde **Settings**. Actualmente solo está disponible para aplicaciones web configuradas mediante Base URL y dominios permitidos. La actividad fuera del alcance web puede conservarse como evidencia, pero no se convierte silenciosamente en automatización.

El Workspace híbrido incluye Projects, Suites, Test Cases, conversión de sesiones grabadas, instrumentación DOM, inteligencia de selectores, Selenium, datos de prueba, secretos seguros, assertions, recovery, self-healing, Suite Runs, reportes HTML, entrega de reportes, ambientes, scheduling/CI, Run Queue, Browser Matrix, Quality Intelligence, versionado, aprobaciones y auditoría.

## Flujo principal del Recorder

1. Crear o nombrar una sesión.
2. Iniciar la grabación y trabajar normalmente sobre el software bajo prueba.
3. Usar **Annotation** para agregar una nota breve dentro del flujo.
4. Usar **Bug** para documentar formalmente un defecto con evidencia, resultado esperado/real, severidad y frecuencia.
5. Finalizar la sesión y revisar pasos/evidencias.
6. Exportar el reporte QA y, cuando corresponda, el reporte de bugs separado.

Las sesiones largas pueden dividirse automáticamente en partes asociadas para mantener los reportes manejables.

## Funcionalidades destacadas del Recorder

- Evidencia cronológica y capturas automáticas.
- Evidence Timeline.
- Session Review.
- Session Bookmarks (`Ctrl+Shift+M`).
- Environment Snapshot.
- Session Templates.
- Manual Checklist.
- Comparación ligera de sesiones.
- Carpetas y tags para organizar historial.
- Privacy Zones y masking de información sensible.
- Smart Checkpoints.
- Coverage Map, Session Insights y Risk Analysis.
- Visual Baselines y Evidence Quality Checker.
- Reusable Flows para regresión manual.

## Automatización web

La automatización web requiere un Project con Base URL válida. QAFlow puede convertir actividad web grabada en Test Cases y ejecutar casos Assisted/Automated mediante Selenium.

Funciones actuales:

- Projects → Suites → Test Cases.
- Manual, Guided, Assisted y Automated.
- DOM instrumentation y Selector Intelligence.
- Test Data y secretos en Windows Credential Manager.
- Assertions.
- Reusable Components.
- Recovery Policies y selector self-healing.
- Runs, Suite Runs y HTML reports.
- Chrome y Edge.
- Environments, scheduling y CI/headless.
- Run Queue, ejecución paralela y Browser Matrix.
- Quality Intelligence: flaky detection, failure clustering y trends.
- Versioning, approval workflow y audit trail.

### Phase 13 · Bloque 1: Smart Waits y assertions de elemento

Esta versión incorpora una primera ampliación del Web Execution Engine:

- espera automática antes de interactuar con un elemento;
- condiciones configurables: `Auto`, `DocumentReady`, `ElementPresent`, `ElementVisible`, `ElementEnabled` y `ElementHidden`;
- timeout y polling configurables por paso;
- assertions específicas sobre el elemento objetivo:
  - `ElementTextContains`;
  - `ElementTextEquals`;
  - `ElementAttributeEquals` usando formato `atributo=valor`;
- los valores de espera se administran junto a Recovery & Self-Healing.

Estas capacidades solo afectan Hybrid Testing. El Recorder normal no depende del motor de automatización.

## Privacidad

QAFlow es local-first. Los datos de sesión y evidencia se almacenan en el equipo local. El Recorder de escritorio no guarda los caracteres escritos por el usuario. Los secretos de automatización se almacenan mediante Windows Credential Manager. Las Privacy Zones pueden ocultar áreas antes de que una captura se escriba en disco.

## Reportes

Según el flujo, QAFlow puede generar:

- PDF de ejecución QA.
- PDF separado de bugs/defectos.
- Reportes HTML offline para automatización.
- Reportes HTML de Suite y Browser Matrix.
- Reportes de Quality Intelligence.
- Paquetes de sesión/evidencia.

Las anotaciones se muestran como notas contextuales en el reporte normal. Los bugs permanecen separados como registros formales de defecto.

## Tecnologías

- **.NET 10** (`net10.0-windows10.0.19041.0`)
- **C#**
- **WPF**
- **Windows UI Automation** y APIs Win32
- **Microsoft WebView2**
- **Selenium WebDriver**
- HTML/JSON local para reportes y persistencia
- **Windows Credential Manager** para secretos
- Extensión local opcional Chrome/Edge para instrumentación DOM
- **Inno Setup 6** para el instalador de Windows

## Requisitos

- Windows 10/11 x64.
- Microsoft Edge WebView2 Runtime.
- Chrome y/o Edge para Hybrid Testing.
- El build publicado self-contained incluye el runtime .NET necesario.

## Compilación

Abrir `QAFlowRecorder.csproj` con Visual Studio compatible con .NET 10.

Para generar el instalador x64 autocontenido:

```text
Installer\Build-Installer.cmd
```

## Extensión de navegador

El paquete también contiene `BrowserExtension`, una edición Manifest V3 independiente para Chrome y Edge. Incluye grabación de interacción web, selectores DOM, screenshots, anotaciones, bugs, revisión de sesión, historial local y exportación HTML/JSON.

La extensión no intenta capturar ni automatizar aplicaciones de escritorio.

## Alcance actual

- **Recorder:** captura flujos web o gráficos visibles de Windows.
- **Automation v1:** exclusivamente web.
- La automatización de escritorio, móvil y API no se presenta como capacidad activa hasta contar con drivers específicos.

## Fase 13 · Bloque 2 — Contexto avanzado del navegador

La automatización híbrida ahora puede declarar, por paso, el contexto exacto del navegador sin afectar el Recorder normal:

- **Pestaña/ventana actual**, primera, última o más reciente.
- Cambio a una pestaña/ventana cuyo **título contenga** un texto configurado.
- Opción para **cerrar la pestaña/ventana actual después de completar el paso y sus validaciones**, evitando cerrar la última ventana disponible.
- Soporte explícito para **iframe** mediante contenido principal, selector CSS, nombre/ID o índice.
- Antes de resolver selectores o ejecutar Smart Waits, QAFlow cambia al contexto configurado y vuelve al documento principal cuando corresponde.
- Se mantiene la protección de **Base URL / dominios permitidos** al cambiar de ventana.

Esta capacidad permite modelar flujos como autenticación externa, checkout con proveedor de pago, ventanas emergentes y aplicaciones que embeben widgets dentro de iframes. No habilita automatización de escritorio; la ejecución sigue siendo exclusivamente web.

## Fase 13 · Bloque 3 — Acciones web avanzadas

La capa híbrida incorpora nuevas acciones ejecutables dentro del navegador sin habilitar automatización de escritorio:

- **DoubleClick**: doble clic sobre el elemento objetivo.
- **RightClick**: abre el menú contextual mediante clic derecho.
- **Hover**: mueve el puntero sobre el elemento para revelar menús o estados dependientes de hover.
- **Clear**: limpia un campo editable sin introducir datos nuevos.
- **Check / Uncheck**: establece de forma idempotente el estado de un checkbox.
- **SelectRadio**: selecciona un radio button si aún no está activo.
- **KeyPress**: envía teclas o combinaciones usando la expresión de valor del paso, por ejemplo `ENTER`, `ESCAPE`, `TAB`, `CTRL+A` o `CTRL+S`.
- **ScrollToElement**: centra el elemento objetivo en el viewport antes de continuar.
- **JavaScript click fallback**: opcional por paso. Solo se utiliza cuando Selenium no puede completar un clic normal y el navegador permite ejecutar JavaScript.

Estas acciones conservan las reglas existentes de **Smart Waits**, contexto de pestañas/ventanas, iframes, URL permitida, retries, assertions y self-healing. El Recorder normal permanece independiente y sin cambios funcionales.



## Fase 13 · Bloque 4 — Archivos y descargas web

La automatización web incorpora dos acciones adicionales sin modificar el comportamiento del Recorder normal:

- **UploadFile**: envía a un control `<input type="file">` la ruta indicada en **Step value expression**. Admite variables de dataset y expansión de variables de entorno. QAFlow valida primero que el archivo exista localmente.
- **ValidateDownload**: espera un archivo nuevo en la carpeta **Downloads** del usuario y valida que la descarga haya finalizado y tenga contenido. En **Step value expression** se puede indicar un nombre exacto (`invoice.pdf`) o un comodín (`*.pdf`). Usa el timeout y polling configurados en Smart Wait.

Las descargas parciales de Chrome/Edge (`.crdownload`, `.part`, `.tmp`) no se consideran válidas. La búsqueda se limita a archivos creados/modificados durante la ejecución actual para reducir falsos positivos.

## Ajuste UX del Recorder normal

La pantalla principal del Recorder se reorganizó para mantener siempre arriba las opciones esenciales: nombre de sesión, tipo de prueba, barra de control, métricas y botón **Iniciar sesión QA**. Plantillas, organización, charter, checklist y flujo de referencia se agrupan debajo en **Opciones avanzadas de sesión**, plegadas por defecto. No se eliminó ninguna funcionalidad existente.


## Fase 13 · Bloque 5 — Estado del navegador y Web Storage

Este bloque se incorpora exclusivamente dentro de las **opciones avanzadas de Hybrid Testing**. No desplaza, oculta ni bloquea los controles normales del Recorder. La pantalla principal mantiene visibles las opciones esenciales y el botón **Iniciar sesión QA**.

Nuevas acciones avanzadas por paso:

- **SetCookie**: crea o actualiza una cookie para el dominio actual. En **Step value expression** usa `nombre=valor`.
- **DeleteCookie**: elimina una cookie por nombre.
- **ClearCookies**: limpia las cookies del navegador para el contexto actual.
- **SetLocalStorage**: escribe un valor en `localStorage` usando `clave=valor`.
- **RemoveLocalStorage**: elimina una clave de `localStorage`.
- **ClearLocalStorage**: limpia `localStorage`.
- **SetSessionStorage**: escribe un valor en `sessionStorage` usando `clave=valor`.
- **RemoveSessionStorage**: elimina una clave de `sessionStorage`.
- **ClearSessionStorage**: limpia `sessionStorage`.

Las expresiones pueden utilizar variables de los Data Sets. Estas operaciones se ejecutan únicamente cuando la URL activa pertenece a la **Base URL** o a los dominios permitidos del proyecto. Las operaciones de Web Storage utilizan el contexto real de la pestaña/iframe configurado para el paso.

Este bloque permite preparar estados de sesión, limpiar datos entre pruebas, activar configuraciones web controladas y reproducir escenarios sin convertir estas capacidades en requisitos del flujo normal de QA.

## Fase 13 · Bloque 6 — Shadow DOM abierto

Este bloque se mantiene exclusivamente dentro de las **opciones avanzadas de Hybrid Testing** y no modifica ni desplaza los controles principales del Recorder normal.

QAFlow puede resolver elementos ubicados dentro de **Shadow DOM abierto** mediante una ruta CSS por segmentos. La sintaxis utiliza `>>>` para separar cada host o nivel, por ejemplo:

```text
my-app >>> qa-panel >>> button.save
```

Características del bloque:

- activación opcional por paso;
- soporte para uno o varios niveles de Shadow DOM abierto;
- integración con Smart Waits, retries, acciones web y assertions existentes;
- compatible con el contexto de pestañas/ventanas e iframes ya configurado;
- no sustituye los selectores DOM normales cuando Shadow DOM está desactivado;
- los **closed shadow roots** no se fuerzan ni se presentan como automatizables, porque el navegador no expone su contenido a JavaScript/Selenium de forma estándar.

Cuando esta opción está activa, QAFlow utiliza la ruta Shadow DOM como destino explícito del paso y evita aplicar self-healing fuera del árbol encapsulado, reduciendo el riesgo de interactuar con un elemento incorrecto.



## Fase 13 · Bloque 7 — Lifecycle avanzado y dependencias controladas

Este bloque se mantiene dentro del espacio avanzado de **Hybrid Testing** y no modifica el flujo principal del Recorder normal.

Se incorporan hooks reutilizables basados en los componentes web existentes:

- **Before Suite**: componente opcional que se ejecuta antes de los casos de la suite.
- **After Suite**: componente opcional de cierre/limpieza que se intenta ejecutar incluso cuando la suite termina con fallo.
- **Before Test**: componente opcional que se ejecuta dentro de la misma sesión de navegador antes de los pasos principales del caso.
- **After Test**: componente opcional de cleanup ejecutado después del flujo principal. QAFlow intenta llegar a este bloque aun cuando un paso principal haya detenido la ejecución.
- **Dependencia controlada entre casos**: un caso puede requerir que otro caso de la misma suite haya aprobado antes de ejecutarse. QAFlow evita guardar dependencias circulares.

Los hooks son totalmente opcionales. Si no se configuran, la ejecución conserva el comportamiento anterior.

## Fase 13 · Bloque 8 — Ejecución Data-driven

Un caso de prueba puede habilitar **Data-driven execution** desde la pestaña avanzada **Lifecycle & Data-driven** y seleccionar varios Data Sets existentes.

Cuando está activo:

- QAFlow ejecuta el mismo caso una vez por cada conjunto de datos seleccionado;
- cada iteración conserva su propio run, evidencia, screenshots y reporte HTML;
- la ejecución individual genera un resumen HTML consolidado cuando existen varias iteraciones;
- Suite Run y el modo headless/CI ejecutan también todas las iteraciones configuradas;
- el reporte de suite identifica el Data Set utilizado en cada resultado;
- las credenciales marcadas como secretas continúan resolviéndose mediante Windows Credential Manager.

La ejecución Data-driven no sustituye el Data Set predeterminado: cuando la opción está desactivada, QAFlow continúa usando el comportamiento anterior de un solo conjunto de datos.

## Fase 13 completada

Con los bloques 1–8, la Fase 13 cubre Smart Waits, contexto de pestañas/ventanas e iframes, acciones web avanzadas, archivos/descargas, estado del navegador, Shadow DOM abierto, lifecycle reutilizable y ejecución Data-driven. Todas estas capacidades pertenecen a **Hybrid Testing** y permanecen separadas de las opciones normales del Recorder.


## Fase 14 · Bloque 1 — Validación visual contra baseline

La automatización web híbrida incorpora una validación visual opcional por paso mediante `VisualBaselineMatch`. Esta capacidad pertenece exclusivamente a las opciones avanzadas de automatización y no altera ni bloquea el flujo normal de QAFlow Recorder.

- El baseline se crea desde la evidencia de un run anterior del mismo paso mediante **Actualizar baseline visual desde último run**.
- Cada baseline se almacena localmente y se identifica por el `Guid` estable del paso automatizado.
- `VisualBaselineMatch` acepta un umbral de similitud entre `0` y `100`; si no se especifica uno, QAFlow usa `95`.
- Durante la ejecución QAFlow captura la imagen actual, la compara con el baseline y registra el porcentaje de similitud en la validación.
- Si no existe baseline, la validación falla de forma explícita en lugar de aprobar silenciosamente.
- Los baselines de automatización se mantienen separados de los baselines visuales del Recorder normal.

Ejemplo: una validación `VisualBaselineMatch` con valor esperado `97` exige al menos **97 % de similitud** con el baseline guardado.


## Fase 14 · Bloque 2 — Diagnóstico técnico del navegador

Este bloque agrega validaciones técnicas **opcionales** dentro de Hybrid Testing. No modifica ni bloquea el Recorder normal y no agrega controles al flujo principal de captura manual.

Nuevas assertions disponibles por paso:

- **NoConsoleErrors**: falla cuando QAFlow detecta `console.error`, errores de JavaScript o promesas no controladas durante el paso.
- **NoNetworkErrors**: falla ante solicitudes `fetch`/XHR con estado HTTP 4xx/5xx, fallos de red detectables o recursos web que no pudieron cargarse.
- **MaxPageLoadMs**: valida que el tiempo de navegación/carga de la página no supere el límite indicado en milisegundos. Ejemplo: `3000`.

QAFlow instala instrumentación local únicamente dentro de la página automatizada y mantiene los resultados asociados al run. Las validaciones se ejecutan como assertions avanzadas y pueden combinarse con Smart Waits, visual baseline, retries, Data-driven, lifecycle y el resto del motor híbrido.

Limitación deliberada: estas comprobaciones utilizan señales disponibles desde el navegador y JavaScript. No se presentan como un proxy de red completo ni sustituyen herramientas dedicadas de observabilidad.


## Fase 14 · Bloque 3 — Presupuestos de rendimiento web

Este bloque amplía las validaciones técnicas opcionales de **Hybrid Testing** con presupuestos de rendimiento por paso. No modifica el Recorder normal ni desplaza sus opciones principales.

Nuevas assertions avanzadas:

- **MaxDomContentLoadedMs**: valida el tiempo hasta `DOMContentLoaded`. Valor inicial sugerido: `2000` ms.
- **MaxFirstContentfulPaintMs**: valida el tiempo hasta First Contentful Paint (FCP) cuando el navegador expone esa métrica. Valor inicial sugerido: `1800` ms.
- **MaxResourceDurationMs**: comprueba la duración del recurso más lento observado por la Performance API. Valor inicial sugerido: `1500` ms.

Estas validaciones se pueden combinar con `MaxPageLoadMs`, diagnósticos de consola/red, Visual Baseline, Smart Waits, Data-driven y Lifecycle. Cada assertion deja su resultado y el valor observado en el run/reporte existente.

QAFlow usa la **Performance API del navegador**, por lo que algunas métricas pueden no estar disponibles en páginas, navegaciones o políticas del navegador específicas. Cuando una métrica no está disponible, la assertion falla de forma explícita en lugar de aprobar silenciosamente.


## Fase 15 · Bloque 1 — API helpers avanzados

QAFlow incorpora una ayuda API opcional para preparar o limpiar datos alrededor de pruebas web sin convertir el producto en una herramienta completa de API testing. Esta capacidad vive únicamente en **Components & Recovery → Advanced web interaction → API helper** y no modifica ni bloquea el flujo normal del Recorder.

- Nueva acción avanzada `ApiRequest`.
- Métodos: `GET`, `POST`, `PUT`, `PATCH`, `DELETE` y `HEAD`.
- La URL puede ser absoluta o relativa a la **Base URL** del proyecto.
- La solicitud se bloquea si intenta salir de la Base URL o de los dominios permitidos.
- Body, URL y headers admiten variables de Data Set; los secretos siguen resolviéndose mediante el mecanismo seguro existente.
- Headers personalizados usan una línea por valor: `Nombre=Valor`.
- `Content-Type` configurable, con `application/json` como valor inicial.
- Código HTTP esperado configurable por paso; si la respuesta no coincide, el paso falla con un mensaje claro.
- El timeout reutiliza **Smart Wait**, por lo que no se agrega otro control de espera paralelo.

Uso recomendado: crear datos de prueba antes de un flujo UI, limpiar registros después de una ejecución o consultar un endpoint de salud del mismo sistema bajo prueba. No se permite usar esta función para llamar silenciosamente a dominios fuera del alcance configurado.


## Fase 15 · Bloque 2 — Ejecución condicional avanzada

QAFlow permite configurar, de forma opcional y solo dentro de Hybrid Testing, condiciones seguras por paso. Si la condición no se cumple, el paso se marca como `Skipped` y no se considera un fallo.

Condiciones disponibles:

- siempre ejecutar;
- ejecutar si existe un elemento CSS;
- ejecutar si no existe un elemento CSS;
- ejecutar si la URL contiene un texto;
- ejecutar si el título contiene un texto;
- ejecutar si una variable/expresión es igual al valor esperado;
- ejecutar si una variable/expresión es distinta al valor esperado.

Las expresiones admiten variables de Data Set. Esta capacidad permanece dentro de opciones avanzadas y no altera la pantalla ni el flujo del Recorder normal.

## Fase 15 · Bloque 3 — Repetición controlada

La automatización híbrida permite repetir un paso de forma explícita y acotada desde **Controlled repetition (advanced)**. El QA puede configurar entre **1 y 20 iteraciones** y un retraso opcional entre iteraciones. Cada iteración conserva Smart Waits, retries, assertions, scope web y políticas de recuperación.

Esta capacidad está pensada para comprobaciones repetidas de estabilidad o comportamiento UI. Para ejecutar el mismo caso con datos distintos debe utilizarse **Data-driven execution**, no repetición. La repetición está desactivada de forma efectiva por defecto (`1` iteración) y no modifica el flujo normal de QAFlow Recorder.
## Fase 15 · Bloque 4 — Self-healing avanzado de selectores

La automatización híbrida incorpora una política de recuperación de selectores configurable por paso dentro de **Components & Recovery**. Esta capacidad es avanzada y no cambia el flujo normal de QAFlow Recorder.

Modos disponibles:

- **Off**: no intenta recuperar el selector.
- **Conservative**: mantiene la recuperación semántica exacta ya existente y es el valor predeterminado para conservar compatibilidad.
- **Advanced**: evalúa candidatos visibles mediante un puntaje de 0 a 100 usando nombre accesible, `role`, etiqueta HTML, `label`, `placeholder`, `title`, texto visible y contexto DOM local.

El modo avanzado incluye un **ambiguity guard**: si los dos mejores candidatos tienen puntajes demasiado cercanos, QAFlow rechaza la recuperación en lugar de arriesgarse a interactuar con el elemento equivocado. El puntaje mínimo es configurable y se limita a un rango seguro.

Cuando se encuentra un candidato confiable, QAFlow intenta construir un selector único priorizando `data-testid`, `id`, `name`, `aria-label`, `role + nombre` y XPath de texto. El usuario puede decidir si el selector recuperado se aprende y pasa a ser el selector principal del paso.

Los reportes HTML registran el mensaje de recuperación, puntaje y estrategia utilizada. Los pasos configurados explícitamente con Shadow DOM mantienen su ruta declarada y el self-healing no sale de ese árbol.

> Esta función no garantiza que cualquier cambio de DOM sea recuperable. Cuando la coincidencia no es suficientemente confiable, QAFlow falla de forma explícita o aplica la política de recuperación configurada.



## Fase 15 · Bloque 5 — Browser Headless real y endurecimiento CI

Este bloque cierra la Fase 15. La ejecución no interactiva de QAFlow y el modo headless del navegador quedan separados para evitar cambios silenciosos en pruebas existentes.

- `--noninteractive`: ejecuta la suite sin abrir la ventana principal de QAFlow.
- `--browser-headless true`: ejecuta Chrome, Edge o Firefox sin ventana visible usando el modo headless correspondiente.
- `--headless` se conserva por compatibilidad como alias histórico de ejecución no interactiva, pero no activa por sí solo el navegador headless.
- El navegador headless usa un viewport fijo de 1920×1080 para mejorar la estabilidad de capturas, layout y comparaciones visuales.
- Schedules y Run Queue disponen de `Browser headless (advanced)` como opción avanzada y desactivada por defecto.
- Las ejecuciones existentes continúan usando navegador visible salvo que el usuario active explícitamente esta opción.

Con este bloque queda cerrada la Fase 15 planificada: API helpers, condiciones, repetición controlada, self-healing avanzado y browser headless real/CI.

## Fase 16 · Bloque 1 — Paridad cross-browser con Firefox

La automatización web amplía su matriz de ejecución a **Firefox**, además de Chrome y Edge. La selección está disponible por proyecto, ambiente, ejecución headless/CI y Run Queue & Browser Matrix. Firefox queda desactivado por defecto en la matriz para no modificar ejecuciones existentes.

- Ejecución Selenium con `FirefoxDriver`.
- Soporte de navegador visible y headless con viewport 1920×1080.
- `--browser Firefox` disponible en CLI/CI.
- Firefox puede combinarse con Chrome y Edge en una misma matriz de regresión.
- Base URL, dominios permitidos, Smart Waits, tabs/windows, iframes, acciones avanzadas, assertions, Data-driven, Lifecycle y reportes continúan aplicando igual.
- La instrumentación DOM mediante la extensión local continúa siendo **Chrome/Edge** en este bloque. En Firefox se pueden ejecutar casos ya definidos, selectores configurados y flujos convertidos, pero no se presenta la extensión Chromium como compatible con Firefox hasta disponer de un paquete específico.

Esta capacidad permanece dentro de Hybrid Testing y no cambia ni desplaza el flujo principal de QAFlow Recorder.



## Fase 16 · Bloque 2 — Instrumentación DOM para Firefox

Este bloque lleva la captura de metadata DOM de QAFlow Hybrid Testing a Firefox sin reemplazar ni alterar la extensión de Chrome/Edge. Cada navegador conserva su carpeta independiente para reducir riesgo de regresiones.

- **Chrome/Edge:** `WebInstrumentationExtension`.
- **Firefox:** `WebInstrumentationExtensionFirefox`.
- Firefox puede cargar la extensión temporalmente desde `about:debugging` → **Este Firefox** → **Cargar complemento temporal**, seleccionando `manifest.json`.
- El bridge sigue escuchando únicamente en `127.0.0.1:47831`.
- La instrumentación continúa respetando Base URL y dominios permitidos.
- No se capturan los valores escritos en `input`, `textarea` ni elementos `contenteditable`.
- Firefox puede enriquecer eventos dentro de iframes abiertos; el payload indica navegador y contexto de frame sin almacenar datos sensibles.
- El panel **Web Instrumentation** muestra el navegador del último cliente conectado y ofrece accesos separados a las carpetas de Chrome/Edge y Firefox.

La instrumentación DOM es una capacidad avanzada de Hybrid Testing. No modifica el Recorder normal ni sus controles principales.

## Fase 16 · Bloque 3 — Análisis diferencial cross-browser

La matriz de ejecución ahora genera automáticamente un **análisis diferencial de compatibilidad** cuando el mismo lote contiene resultados de dos o más navegadores. El objetivo es detectar divergencias reales entre Chrome, Edge y Firefox sin cambiar la lógica de los casos ni agregar fricción al flujo normal del QA.

- Compara cada caso por identidad entre los navegadores ejecutados en el mismo batch.
- Clasifica los casos como **Consistent**, **Needs review**, **Different result** o **Missing result**.
- Muestra por navegador el estado, pass rate y duración del mismo caso.
- Incluye un resumen de mensajes de fallo para acelerar el diagnóstico de diferencias específicas de navegador.
- El reporte de Browser Matrix presenta un veredicto general y métricas de casos consistentes, diferentes/ausentes y pendientes de revisión.
- El análisis es de solo lectura: no modifica selectores, retries, assertions, datos, ambientes ni ejecución.
- Si solo se ejecuta un navegador, QAFlow mantiene el reporte normal y explica que se requieren al menos dos navegadores para comparar.

Este bloque continúa dentro de **Hybrid Testing / Run Queue & Browser Matrix**. El Recorder normal y sus opciones principales permanecen visibles e independientes.

## Fase 16 · Bloque 4 — Diagnóstico previo de navegadores

La matriz cross-browser incorpora un **preflight opcional de capacidades locales** antes de ejecutar Chrome, Edge y Firefox. El objetivo es detectar rápidamente instalaciones ausentes o rutas no estándar sin bloquear las opciones normales del QAFlow Recorder ni modificar la ejecución existente.

- Nuevo botón **Browser preflight / Diagnóstico de navegadores** dentro de `Run Queue & Browser Matrix`.
- Comprueba los navegadores seleccionados en ubicaciones estándar de Windows, `App Paths` del Registro y `PATH`.
- Informa la ruta detectada de Chrome, Edge o Firefox cuando está disponible.
- Una ausencia se presenta como advertencia y **no bloquea** la matriz, porque Selenium Manager todavía puede resolver instalaciones portables o personalizadas.
- El diagnóstico no instala software, no descarga drivers y no cambia los casos de prueba.
- Se conserva el análisis diferencial del Bloque 3 y toda la instrumentación Firefox/Chromium de los bloques anteriores.
- El Recorder normal permanece independiente y sus opciones principales no se desplazan ni se deshabilitan.

## Fase 16 · Bloque 5 — Huella/versionado del navegador

Este bloque mejora la trazabilidad de la matriz cross-browser sin modificar la ejecución normal del Recorder.

- El diagnóstico previo ahora muestra la **versión detectada** de Chrome, Edge y Firefox junto con la ruta del ejecutable.
- Al encolar una matriz, QAFlow guarda una instantánea de `BrowserVersion` y `BrowserExecutablePath` para cada trabajo.
- Justo antes de iniciar cada trabajo, QAFlow vuelve a consultar el navegador para registrar la versión efectiva disponible en ese momento.
- La cola muestra una columna **Version** para identificar rápidamente qué build del navegador se probó.
- El reporte Browser Matrix incluye la versión por navegador y también la muestra dentro del análisis diferencial por caso.
- Si la versión no puede detectarse mediante rutas estándar, la ejecución no se bloquea: Selenium Manager conserva la posibilidad de resolver instalaciones personalizadas/portables.
- Los datos nuevos son opcionales y compatibles con colas guardadas por versiones anteriores.
- Se conservan el preflight del Bloque 4, el análisis diferencial del Bloque 3, Firefox DOM Instrumentation y los fixes del instalador (`MAX_PATH` y restore `win-x64`).
- El Recorder normal permanece independiente, visible y sin cambios funcionales.


## Fase 16 · Bloque 6 — Evidencia comparativa cross-browser

La matriz de navegadores amplía el reporte diferencial con una sección de **evidencia comparativa lado a lado**. Esta función utiliza las capturas ya producidas por las ejecuciones y no modifica los casos, selectores ni resultados.

- Para cada caso comparable entre dos o más navegadores, el reporte puede mostrar la evidencia capturada por Chrome, Edge y Firefox en tarjetas paralelas.
- Se priorizan automáticamente los casos con resultados diferentes, fallidos o en revisión.
- Cuando un caso consistente dispone de capturas en al menos dos navegadores, también puede mostrarse para inspección visual.
- Cada tarjeta identifica navegador y versión, estado, pass rate, duración, paso representativo y acceso al reporte individual del caso.
- En casos con fallo se prioriza la captura del paso fallido; de no existir, se utiliza la evidencia más reciente disponible.
- Si falta una captura, el reporte lo indica explícitamente en lugar de inferir una diferencia visual.
- La comparación es diagnóstica y de solo lectura: QAFlow no aprueba ni rechaza automáticamente una diferencia únicamente por comparar imágenes.

Este bloque conserva el análisis diferencial del Bloque 3, el preflight del Bloque 4, la huella de navegador del Bloque 5, la instrumentación DOM para Firefox y las correcciones del instalador.

### Corrección de apertura de la ventana principal

También se corrigió el movimiento visual que ocurría al iniciar QAFlow Recorder. La ventana principal ahora calcula su posición a la derecha **antes de mostrarse**, en lugar de aparecer primero en una posición inicial y desplazarse después durante `Loaded`. El resultado es una apertura directa en el lado derecho de la pantalla, sin salto visible ni movimiento extraño.


## Fase 16 · Bloque 7 — Resumen ejecutivo y cierre cross-browser

Este bloque cierra formalmente la Fase 16 sin alterar el Recorder normal ni cambiar la lógica de ejecución de los casos. El reporte de Browser Matrix ahora incluye una capa ejecutiva de consolidación para facilitar el cierre QA del batch.

- Nuevo **Phase 16 executive summary** dentro del reporte Browser Matrix.
- Consolida trabajos de navegador completados, cobertura de casos comparables, diferencias cross-browser y elementos pendientes de revisión.
- Produce un estado de cierre orientativo: matriz incompleta, atención requerida, lista para revisión QA o lista para sign-off QA.
- Genera acciones recomendadas según resultados reales: ejecuciones pendientes, navegadores fallidos, casos con resultados diferentes, casos ausentes y revisiones manuales.
- La decisión final sigue siendo humana: el resumen no aprueba releases ni altera estados de casos automáticamente.

## 📥 Instalación

1. Descarga el ejecutable desde la sección de **[Releases Oficiales](https://github.com/lcalero85/QAFlow-Recorder/releases/latest)**.
2. Ejecuta el archivo `QAFlow_Recorder_1.0_Setup_x64.exe`.
3. Si Windows SmartScreen muestra una advertencia, haz clic en **"Más información"** y luego en **"Ejecutar de todas formas"**.
