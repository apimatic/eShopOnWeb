using System.Diagnostics;

namespace SquareCheck;

internal interface IProcessLauncher
{
    void OpenUrl(string url);
}

internal sealed class SystemProcessLauncher : IProcessLauncher
{
    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            Console.Error.WriteLine("Could not open the browser automatically. Please navigate to:");
            Console.Error.WriteLine(url);
        }
    }
}
