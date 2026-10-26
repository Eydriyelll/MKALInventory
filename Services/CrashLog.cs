namespace MkalInventoryApp.Services;

/// <summary>Diagnostic log (next to the app and on the Desktop). Never throws.</summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string[] Paths => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "mkal_crash.log"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "mkal_crash.log")
    };

    public static void Write(string source, Exception? ex) =>
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR {source}{Environment.NewLine}{ex}{Environment.NewLine}");

    public static void Note(string message) =>
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");

    private static void Append(string text)
    {
        System.Diagnostics.Debug.WriteLine(text);
        lock (Gate)
        {
            foreach (var path in Paths)
            {
                try { File.AppendAllText(path, text); }
                catch { /* logging must never throw */ }
            }
        }
    }
}
