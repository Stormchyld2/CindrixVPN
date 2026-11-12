namespace CindrixVPN.Services;

public static class Log
{
    private static readonly object Gate = new();

    public static event Action<string>? Line;

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.Logs);
                File.AppendAllText(Paths.LogFile, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never break the app.
            }
        }
        Line?.Invoke(line);
    }

    public static IEnumerable<string> Recent(int count)
    {
        try
        {
            return File.Exists(Paths.LogFile) ? File.ReadLines(Paths.LogFile).TakeLast(count).ToList() : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
