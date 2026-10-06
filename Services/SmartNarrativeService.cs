using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class SmartNarrativeService
{
    public static string Describe(QaEvent e, bool es)
    {
        if (!string.IsNullOrWhiteSpace(e.CustomDescription)) return e.CustomDescription.Trim();
        var target = Target(e, es);
        var control = Control(e, es);
        return e.Kind switch
        {
            EventKind.Click when !string.IsNullOrWhiteSpace(control) => es ? $"Seleccionó {control} en {target}." : $"Selected {control} in {target}.",
            EventKind.Click => es ? $"Realizó un clic en {target}." : $"Clicked in {target}.",
            EventKind.Input when !string.IsNullOrWhiteSpace(control) => es ? $"Ingresó o modificó información en {control}, dentro de {target}." : $"Entered or modified information in {control}, within {target}.",
            EventKind.Input => es ? $"Ingresó o modificó información en {target}." : $"Entered or modified information in {target}.",
            EventKind.Select when !string.IsNullOrWhiteSpace(control) => es ? $"Seleccionó una opción en {control}, dentro de {target}." : $"Selected an option in {control}, within {target}.",
            EventKind.Select => es ? $"Seleccionó una opción en {target}." : $"Selected an option in {target}.",
            EventKind.Submit when !string.IsNullOrWhiteSpace(control) => es ? $"Confirmó o envió información mediante {control} en {target}." : $"Confirmed or submitted information using {control} in {target}.",
            EventKind.Submit => es ? $"Confirmó o envió información en {target}." : $"Confirmed or submitted information in {target}.",
            EventKind.Scroll when !string.IsNullOrWhiteSpace(control) => es ? $"Se desplazó por {control} en {target}." : $"Scrolled through {control} in {target}.",
            EventKind.Scroll => es ? $"Se desplazó por {target}." : $"Scrolled through {target}.",
            EventKind.Dialog => es ? $"Interactuó con un cuadro de diálogo en {target}." : $"Interacted with a dialog in {target}.",
            EventKind.ConsoleError => es ? $"Se detectó un error técnico mientras se trabajaba en {target}." : $"A technical error was detected while working in {target}.",
            EventKind.HttpError => es ? $"Se detectó una respuesta HTTP fallida durante la interacción con {target}." : $"A failed HTTP response was detected while interacting with {target}.",
            EventKind.ResourceError => es ? $"Se detectó un recurso que no pudo cargarse en {target}." : $"A resource failed to load in {target}.",
            _ => es ? $"Se registró una interacción en {target}." : $"An interaction was recorded in {target}."
        };
    }

    public static string CheckpointTitle(QaEvent e, bool es)
    {
        var title = string.IsNullOrWhiteSpace(e.CheckpointTitle) ? Target(e, es) : e.CheckpointTitle.Trim();
        var reason = e.CheckpointKind switch
        {
            "SessionStart" => es ? "Inicio del flujo" : "Flow start",
            "ApplicationChange" => es ? "Cambio de aplicación" : "Application change",
            "WindowChange" => es ? "Nueva pantalla o ventana" : "New screen or window",
            "LongPause" => es ? "Reanudación después de una pausa" : "Resumed after a pause",
            "Submit" => es ? "Confirmación de datos" : "Data confirmation",
            "Dialog" => es ? "Diálogo relevante" : "Relevant dialog",
            "Finding" => es ? "Hallazgo detectado" : "Finding detected",
            _ => es ? "Hito QA" : "QA checkpoint"
        };
        return $"{reason} — {title}";
    }

    private static string Control(QaEvent e, bool es)
    {
        var name=(e.ControlName ?? "").Trim();
        var type=LocalControlType(e.ControlType,es);
        if(!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type)) return $"‘{name}’ ({type})";
        if(!string.IsNullOrWhiteSpace(name)) return $"‘{name}’";
        if(!string.IsNullOrWhiteSpace(type) && !type.Equals(es?"panel":"pane",StringComparison.OrdinalIgnoreCase) && !type.Equals(es?"documento":"document",StringComparison.OrdinalIgnoreCase)) return es?$"el control {type}":$"the {type}";
        return "";
    }

    private static string LocalControlType(string? value,bool es)
    {
        var type=(value ?? "").Trim();
        if(string.IsNullOrWhiteSpace(type)) return "";
        if(!es) return type.ToLowerInvariant();
        return type.ToLowerInvariant() switch
        {
            "button"=>"botón", "edit"=>"campo de texto", "combobox"=>"lista desplegable", "listitem"=>"elemento de lista",
            "menuitem"=>"opción de menú", "checkbox"=>"casilla", "radiobutton"=>"opción", "hyperlink"=>"enlace",
            "tabitem"=>"pestaña", "treeitem"=>"elemento de árbol", "document"=>"documento", "pane"=>"panel",
            _=>type.ToLowerInvariant()
        };
    }

    private static string Target(QaEvent e, bool es)
    {
        var app = FriendlyAppName(e.ApplicationName);
        var title = !string.IsNullOrWhiteSpace(e.WindowTitle) ? e.WindowTitle.Trim() : e.TargetTitle.Trim();
        if (!string.IsNullOrWhiteSpace(app) && !string.IsNullOrWhiteSpace(title) && !string.Equals(app, title, StringComparison.OrdinalIgnoreCase))
            return $"{app} — {title}";
        if (!string.IsNullOrWhiteSpace(title)) return title;
        if (!string.IsNullOrWhiteSpace(app)) return app;
        return es ? "el escritorio de Windows" : "the Windows desktop";
    }

    private static string FriendlyAppName(string? value)
    {
        var app=(value ?? "").Trim();
        if(string.IsNullOrWhiteSpace(app)) return "";
        return app.ToLowerInvariant() switch
        {
            "chrome"=>"Google Chrome", "msedge"=>"Microsoft Edge", "firefox"=>"Mozilla Firefox", "explorer"=>"File Explorer",
            "notepad"=>"Notepad", "devenv"=>"Visual Studio", "code"=>"Visual Studio Code", "winword"=>"Microsoft Word",
            "excel"=>"Microsoft Excel", "powerpnt"=>"Microsoft PowerPoint", "outlook"=>"Microsoft Outlook",
            _=>app
        };
    }
}
