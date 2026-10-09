using Microsoft.eShopWeb.SquareCheck;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.SignIn;
using Microsoft.eShopWeb.SquareCheck.SquareAccess;

using var stop = new CancellationTokenSource();
ConsoleCancelEventHandler onCtrlC = (_, e) =>
{
    // First Ctrl+C: stop the run cleanly (exit 130). A second one falls through to the default hard stop.
    if (!stop.IsCancellationRequested)
    {
        e.Cancel = true;
        stop.Cancel();
    }
};
Console.CancelKeyPress += onCtrlC;

try
{
    if (!SquareSettingsValidator.TryValidate(SquareConfiguration.Load(), out var settings, out var problems))
    {
        Console.Error.WriteLine(
            $"SquareCheck is not configured: {string.Join("; ", problems)}. "
            + "Set them with 'dotnet user-secrets' or the SQUARE_* environment variables.");
        return ExitCodes.SetupProblem;
    }

    using var squareHttp = SquareClientFactory.CreateHttpClient();
    var app = new SquareCheckApp(settings, squareHttp, new SystemBrowserLauncher(), Console.Out, Console.Error);
    return await app.RunAsync(stop.Token);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return ExitCodes.Cancelled;
}
catch (Exception ex)
{
    // One plain line, never a stack trace.
    Console.Error.WriteLine($"SquareCheck stopped unexpectedly: {ex.GetType().Name}: {ex.Message}");
    return ExitCodes.Unexpected;
}
finally
{
    Console.CancelKeyPress -= onCtrlC;
}
