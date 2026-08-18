using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Removes anything that looks like a phone number from provider error text before it can travel into an
/// exception message, a stored record, or a log line — a shopper's number must never reach the logs.
/// </summary>
public static partial class PhoneRedactor
{
    [GeneratedRegex(@"\+?\d[\d\-\s().]{6,}\d")]
    private static partial Regex PhoneLike();

    public static string Redact(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : PhoneLike().Replace(text, "[redacted]");
}
