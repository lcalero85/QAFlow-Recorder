QAFlow Web Instrumentation for Firefox

Instalación temporal para desarrollo/pruebas:
1. Abre about:debugging#/runtime/this-firefox
2. Selecciona "Cargar complemento temporal".
3. Elige manifest.json dentro de esta carpeta.
4. Mantén QAFlow Recorder abierto en Hybrid Testing con Base URL y dominios permitidos configurados.

Privacidad:
- Se comunica solo con http://127.0.0.1:47831.
- No captura valores escritos en inputs/textarea/contenteditable.
- Solo envía metadata DOM de URLs dentro del alcance permitido por QAFlow.
- Puede observar iframes abiertos para enriquecer selectores y contexto de frames.

Esta carpeta es independiente de WebInstrumentationExtension (Chrome/Edge) para evitar regresiones entre navegadores.
