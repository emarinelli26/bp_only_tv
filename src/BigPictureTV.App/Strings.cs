using System.Globalization;

namespace BigPictureTV.App;

/// <summary>UI text in English and Spanish, picked from the Windows display language.</summary>
public sealed class Strings
{
    public static Strings Current { get; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? Spanish() : English();

    public string AlreadyRunning { get; init; } = "";
    public string StateDesktop { get; init; } = "";
    public string StateTvAuto { get; init; } = "";
    public string StateTvManual { get; init; } = "";
    public string Paused { get; init; } = "";
    public string SwitchToTv { get; init; } = "";
    public string BackToDesktop { get; init; } = "";
    public string PauseAutomatic { get; init; } = "";
    public string ChooseTv { get; init; } = "";
    public string DetectAutomatically { get; init; } = "";
    public string DetectedNone { get; init; } = "";
    public string NoDisplays { get; init; } = "";
    public string StartWithWindows { get; init; } = "";
    public string OpenLogFolder { get; init; } = "";
    public string Exit { get; init; } = "";
    public string NowOnTv { get; init; } = "";
    public string NowOnDesktop { get; init; } = "";
    public string SwitchFailed { get; init; } = "";
    public string ChangedOutside { get; init; } = "";

    static Strings English() => new()
    {
        AlreadyRunning = "BigPictureTV is already running. Look for its icon next to the clock.",
        StateDesktop = "Desktop",
        StateTvAuto = "On the TV (Big Picture)",
        StateTvManual = "On the TV",
        Paused = "paused",
        SwitchToTv = "Switch to the TV now",
        BackToDesktop = "Back to the desktop",
        PauseAutomatic = "Pause automatic switching",
        ChooseTv = "TV",
        DetectAutomatically = "Detect automatically",
        DetectedNone = "nothing detected",
        NoDisplays = "No displays found",
        StartWithWindows = "Start with Windows",
        OpenLogFolder = "Open log folder",
        Exit = "Exit",
        NowOnTv = "Showing only the TV.",
        NowOnDesktop = "Desktop restored.",
        SwitchFailed = "Couldn't switch to the TV. Check that it's on, or pick it under TV.",
        ChangedOutside = "Windows turned the other displays back on. Big Picture will switch to the TV again next time it opens.",
    };

    static Strings Spanish() => new()
    {
        AlreadyRunning = "BigPictureTV ya está abierto. Buscá su ícono junto al reloj.",
        StateDesktop = "Escritorio",
        StateTvAuto = "En la TV (Big Picture)",
        StateTvManual = "En la TV",
        Paused = "en pausa",
        SwitchToTv = "Pasar a la TV ahora",
        BackToDesktop = "Volver al escritorio",
        PauseAutomatic = "Pausar el cambio automático",
        ChooseTv = "TV",
        DetectAutomatically = "Detectar automáticamente",
        DetectedNone = "no se detectó ninguna",
        NoDisplays = "No se encontraron pantallas",
        StartWithWindows = "Iniciar con Windows",
        OpenLogFolder = "Abrir carpeta de registros",
        Exit = "Salir",
        NowOnTv = "Mostrando solo la TV.",
        NowOnDesktop = "Escritorio restaurado.",
        SwitchFailed = "No se pudo pasar a la TV. Revisá que esté encendida o elegila en el menú TV.",
        ChangedOutside = "Windows volvió a encender las otras pantallas. La próxima vez que abras Big Picture pasa a la TV de nuevo.",
    };
}
