using Microsoft.eShopWeb.SquareCheck;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.SignIn;

// SquareCheck — signs in to the shop's Square account through the browser and shows what it is connected to.
// Exit codes: 0 connected, 1 Square refused/failed, 2 sign-in declined or not finished, 3 cannot start, 130 Ctrl+C.

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (cancellation.IsCancellationRequested)
    {
        return; // A second Ctrl+C terminates immediately.
    }

    e.Cancel = true;
    cancellation.Cancel();

    // Everything honours the token; this is only a backstop so Ctrl+C always ends the run promptly.
    _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => Environment.Exit(ExitCodes.Cancelled), TaskScheduler.Default);
};

SquareSettings settings;
try
{
    var result = SquareSettings.Load(SquareConfiguration.Build(Environment.GetEnvironmentVariables()));
    if (result.Settings is null)
    {
        Console.Error.WriteLine(string.Join(" ", result.Errors));
        return ExitCodes.CannotStart;
    }

    settings = result.Settings;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not read SquareCheck's configuration: {FailureClassifier.OneLine(ex.Message)}");
    return ExitCodes.CannotStart;
}

try
{
    var app = new SquareCheckApp(settings, new SystemBrowserLauncher(), Console.Out, Console.Error, SquareCheckTimeouts.Default);
    return await app.RunAsync(cancellation.Token);
}
catch (Exception ex)
{
    if (cancellation.IsCancellationRequested)
    {
        Console.Error.WriteLine("Cancelled.");
        return ExitCodes.Cancelled;
    }

    Console.Error.WriteLine($"SquareCheck failed unexpectedly: {FailureClassifier.OneLine(ex.Message)}");
    return ExitCodes.SquareFailure;
}
