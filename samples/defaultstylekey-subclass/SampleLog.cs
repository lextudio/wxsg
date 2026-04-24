using System;
using System.IO;

namespace DefaultStyleKeySubclassSample;

internal static class SampleLog
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "wxsg-defaultstylekey-sample.txt");

    static SampleLog()
    {
        File.WriteAllText(LogPath, $"--- run {DateTime.Now:O} ---\n");
    }

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\n");
        }
        catch { }
    }
}
