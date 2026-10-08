using System.IO;

namespace PhantomFudge.App;

internal enum WindowFailure { Startup, CaptureSetup }

internal static class LocalFailureNotice
{
    public static void Record(WindowFailure failure)
    {
        var message = failure switch
        {
            WindowFailure.Startup => "The protected window or local recovery channel could not initialize. No notes were loaded.",
            WindowFailure.CaptureSetup => "Capture protection could not be configured. The window remains hidden; unsaved text remains in the running process.",
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        Console.Error.WriteLine(message);
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhantomFudge");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "last-window-error.txt"),
                $"{DateTimeOffset.UtcNow:o}{Environment.NewLine}{message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("The local window-error notice could not be written.");
        }
    }
}
