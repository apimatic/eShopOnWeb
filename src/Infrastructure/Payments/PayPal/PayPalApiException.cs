using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public class PayPalApiException : PayPalGatewayException
{
    public PayPalApiException(int statusCode, string message, string? paypalName, string? debugId, string? issue)
        : base(statusCode, message, paypalName, debugId, issue)
    {
    }

    public static PayPalApiException From(HttpStatusCode statusCode, string body)
    {
        var parsed = PayPalJson.Deserialize<PayPalErrorBody>(body);
        var issue = parsed?.Details is { Count: > 0 } ? parsed.Details[0].Issue : null;
        var detail = parsed?.Details is { Count: > 0 } ? parsed.Details[0].Description : null;

        var message = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(parsed?.Message))
        {
            message.Append(parsed!.Message);
        }
        else
        {
            message.Append("PayPal request failed.");
        }

        if (!string.IsNullOrWhiteSpace(issue))
        {
            message.Append(" Issue: ").Append(issue);
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            message.Append(" ").Append(detail);
        }

        if (!string.IsNullOrWhiteSpace(parsed?.DebugId))
        {
            message.Append(" (debug_id ").Append(parsed!.DebugId).Append(')');
        }

        var code = (int)statusCode;
        if (code is < 400 or > 599)
        {
            code = 502;
        }

        return new PayPalApiException(code, message.ToString(), parsed?.Name, parsed?.DebugId, issue);
    }
}
