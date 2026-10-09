using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.eShopWeb.SquareCheck.SignIn;

public interface IBrowserLauncher
{
    /// <summary>
    /// Opens <paramref name="url"/> in the operator's browser. Returns <c>false</c> when no browser could be started.
    /// </summary>
    bool TryOpen(string url);
}

/// <summary>
/// Opens the operating system's default browser.
/// </summary>
public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public bool TryOpen(string url)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo = new ProcessStartInfo(url) { UseShellExecute = true };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo = new ProcessStartInfo("open") { ArgumentList = { url } };
            }
            else
            {
                startInfo = new ProcessStartInfo("xdg-open") { ArgumentList = { url } };
            }

            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
