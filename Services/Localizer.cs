namespace QAFlowRecorder.Services;

public static class Localizer
{
    private static bool Es(string language) => language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) || language.Equals("ES", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string,(string En,string Es)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NoSession"]=("No session","Sin sesión"), ["Recorder"]=("Recorder","Grabador"), ["History"]=("History","Historial"), ["Compare"]=("Compare","Comparar"),
        ["Settings"]=("Settings","Configuración"), ["DocumentTitle"]=("Document your QA tests","Documenta tus pruebas QA"),
        ["DocumentSubtitle"]=("When recording starts, the main panel hides. Use the floating bar or global hotkeys to control the session.","Al iniciar la grabación, el panel principal se oculta. Usa la barra flotante o los atajos globales para controlar la sesión."),
        ["SessionName"]=("Session name","Nombre de la sesión"), ["TestType"]=("Test type","Tipo de prueba"), ["ShowBar"]=("Show movable top bar while recording","Mostrar barra superior móvil durante la grabación"),
        ["Start"]=("Start recording session","Iniciar sesión de grabación"), ["Pause"]=("Pause","Pausar"), ["Resume"]=("Resume","Reanudar"), ["Finish"]=("Finish","Finalizar"),
        ["Annotate"]=("Annotate","Anotar"), ["Capture"]=("Capture","Captura"), ["Bug"]=("Bug","Bug"), ["OpenDetails"]=("Open browser and details","Abrir navegador y detalles"), ["Open"]=("Open","Abrir"),
        ["More"]=("More actions","Más acciones"), ["Privacy"]=("PRIVACY","PRIVACIDAD"), ["PrivacyText"]=("Local recording. Data stays on this PC and sensitive fields are masked.","Grabación local. Los datos permanecen en este equipo y los campos sensibles se enmascaran."),
        ["QaName"]=("QA name","Nombre del QA"), ["AutoNav"]=("Automatic capture on navigation","Captura automática al navegar"), ["BeforeSubmit"]=("Capture before submit","Captura antes de enviar formularios"), ["Periodic"]=("Periodic capture","Captura periódica"), ["Interval"]=("Periodic interval (seconds)","Intervalo periódico (segundos)"),
        ["Appendix"]=("Include technical appendix","Incluir apéndice técnico"), ["Quality"]=("Image quality","Calidad de imagen"), ["DocLanguage"]=("Document language","Idioma del documento"), ["ExportFolder"]=("Export folder","Carpeta de exportación"), ["SaveSettings"]=("Save settings","Guardar configuración"),
        ["ExportTitle"]=("Export session","Exportar sesión"), ["TestCase"]=("Session record","Registro de sesión"), ["QaLog"]=("QA execution report (PDF)","Reporte de ejecución QA (PDF)"),
        ["BugPdf"]=("Bug report (PDF)","Reporte de bugs (PDF)"), ["Zip"]=("Package as ZIP","Empaquetar en ZIP"), ["Cancel"]=("Cancel","Cancelar"), ["Export"]=("Export selected","Exportar seleccionados"),
        ["Recording"]=("RECORDING","GRABANDO"), ["Paused"]=("PAUSED","PAUSADO"), ["Finished"]=("FINISHED","FINALIZADA"), ["Historical"]=("HISTORY","HISTÓRICO"), ["Imported"]=("IMPORTED","IMPORTADA"),
        ["BugTitle"]=("Report bug — recording paused","Reportar bug — grabación en pausa"), ["BugPauseInfo"]=("Recording is paused while you document the bug. It resumes automatically when you save or close.","La grabación está pausada mientras documentas el bug. Se reanuda automáticamente al guardar o cerrar."),
        ["BugScreen"]=("Screen / module where the issue occurred","Pantalla / módulo donde ocurrió el problema"), ["Frequency"]=("Bug frequency","Frecuencia del bug"), ["ResultType"]=("Result type","Tipo de resultado"), ["Severity"]=("Severity","Severidad"),
        ["Expected"]=("Expected result *","Resultado esperado *"), ["Actual"]=("Actual result","Resultado real"), ["Attachments"]=("Attachments","Adjuntos / anexos"), ["AddImage"]=("Add image","Agregar imagen"), ["FreeNote"]=("Additional notes","Notas adicionales"), ["SaveResume"]=("Save and resume","Guardar y reanudar"),
        ["MarkupTitle"]=("Mark screenshot — QAFlow","Marcar captura — QAFlow"), ["Rectangle"]=("Rectangle","Rectángulo"), ["Circle"]=("Circle","Círculo"), ["Arrow"]=("Arrow","Flecha"), ["Line"]=("Line","Línea"), ["Text"]=("Text","Texto"), ["Callout"]=("Callout","Llamada"), ["Undo"]=("Undo","Deshacer"), ["SavePlain"]=("Save without marks","Guardar sin marcas"), ["SaveMarked"]=("Save marked","Guardar marcada"),
        ["Time"]=("TIME","TIEMPO"), ["Actions"]=("ACTIONS","ACCIONES"), ["Pages"]=("PAGES","PÁGINAS"), ["NotesErrors"]=("NOTES / ERR.","NOTAS / ERR."),
        ["SessionStarted"]=("Session started","Sesión iniciada"), ["AnnotationSaved"]=("Annotation saved","Anotación guardada"), ["SettingsSaved"]=("Settings saved","Configuración guardada"),
        ["Exporting"]=("Generating export files…","Generando archivos de exportación…"), ["Exported"]=("Exported","Exportado"),
        ["Ready"]=("Ready","Listo"), ["FlowEvidence"]=("Flow evidence","Evidencia del flujo")
    };

    public static string T(string key, string language)
    {
        if (!Map.TryGetValue(key, out var pair)) return key;
        return Es(language) ? pair.Es : pair.En;
    }
}
