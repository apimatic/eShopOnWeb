using System.Diagnostics;

namespace Microsoft.eShopWeb.SquareCheck.SignIn;

public interface IBrowserLauncher
{
    /// <summary>Opens <paramref name="address"/> in the operator's browser. Returns false when it could not.</summary>
    bool TryOpen(Uri address);
}

public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public bool TryOpen(Uri address)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
