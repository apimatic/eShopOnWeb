using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

internal static class CardDataRedactor
{
    private static readonly Regex PanPattern = new(@"\b[0-9]{13,19}\b", RegexOptions.Compiled);
    private static readonly Regex CvvPattern = new(@"""security_code""\s*:\s*""[0-9]{3,4}""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = PanPattern.Replace(value, "[REDACTED]");
        redacted = CvvPattern.Replace(redacted, "\"security_code\":\"[REDACTED]\"");
        return redacted;
    }

    public static string DescribeWithoutSecrets(string method, string path, int statusCode, string? debugId)
    {
        var builder = new StringBuilder();
        builder.Append(method).Append(' ').Append(path).Append(" -> ").Append(statusCode);
        if (!string.IsNullOrEmpty(debugId))
        {
            builder.Append(" debug_id=").Append(debugId);
        }

        return builder.ToString();
    }
}
