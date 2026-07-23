using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Writes the billing seam's diagnostics straight to the console. The seed tool is a one-shot operator
/// utility, so it does not take a dependency on the hosts' logging stack.
/// </summary>
public class ConsoleAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args) => Write("info", message, args);

    public void LogWarning(string message, params object[] args) => Write("warn", message, args);

    private static void Write(string level, string message, params object[] args)
    {
        var text = args.Length == 0 ? message : Format(message, args);
        Console.WriteLine($"  [{level}] {text}");
    }

    private static string Format(string message, object[] args)
    {
        var text = message;
        for (var i = 0; i < args.Length; i++)
        {
            text = text.Replace($"{{{i}}}", args[i]?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        return text;
    }
}
