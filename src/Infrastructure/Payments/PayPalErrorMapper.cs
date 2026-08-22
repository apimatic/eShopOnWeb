using System.Linq;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalErrorMapper
{
    public static PaymentOperationException FromError(Error error, int statusCode)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        var description = error.Details?.FirstOrDefault()?.Description;
        return new PaymentOperationException(
            Format(error.Name, error.Message, issue, description, error.DebugId),
            Normalize(statusCode),
            error.DebugId,
            issue);
    }

    public static PaymentOperationException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        var body = SafeBody(raw);
        return new PaymentOperationException(
            string.IsNullOrWhiteSpace(body)
                ? "The payment processor rejected the request."
                : $"The payment processor rejected the request: {body}",
            Normalize(status));
    }

    public static ApiException FromJsonException()
    {
        var status = PayPalLastStatus.Current;
        if (status is >= 400 and < 500)
        {
            return new ApiException("The payment processor rejected the request.", status.Value);
        }

        return new ApiException("The payment processor returned a response that could not be processed.", 502);
    }

    public static ApiException Unreachable()
    {
        return new ApiException("The payment processor is unreachable.", 503);
    }

    public static ApiException DuplicateWrite()
    {
        return new ApiException(
            "The payment request may already have reached the processor. Refresh the order and retry only if it is still unpaid.",
            409);
    }

    public static ApiException PayerActionRequired()
    {
        return new PaymentOperationException(
            "PayPal required a shopper approval / 3-D Secure challenge in the browser. This integration does not implement a browser round-trip (GAP).",
            409,
            issue: "PAYER_ACTION_REQUIRED");
    }

    private static int Normalize(int statusCode)
    {
        if (statusCode >= 500)
        {
            return 502;
        }

        if (statusCode < 400)
        {
            return 422;
        }

        return statusCode;
    }

    private static string Format(string name, string message, string? issue, string? description, string? debugId)
    {
        var sb = new StringBuilder();
        sb.Append(name);
        sb.Append(": ");
        sb.Append(message);
        if (!string.IsNullOrEmpty(issue))
        {
            sb.Append(" [");
            sb.Append(issue);
            sb.Append(']');
        }
        if (!string.IsNullOrEmpty(description))
        {
            sb.Append(' ');
            sb.Append(description);
        }
        if (!string.IsNullOrEmpty(debugId))
        {
            sb.Append(" (PayPal debug id: ");
            sb.Append(debugId);
            sb.Append(')');
        }

        return sb.ToString();
    }

    private static string SafeBody(RawError raw)
    {
        try
        {
            var text = raw.ReadAsString();
            if (string.IsNullOrWhiteSpace(text) || text.Length > 500)
            {
                return string.Empty;
            }

            return text;
        }
        catch
        {
            return string.Empty;
        }
    }
}
