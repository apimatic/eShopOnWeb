using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalErrorTranslator
{
    public static PaymentGatewayException FromError(Error error, int fallbackStatus)
    {
        var details = error.Details is null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => $"{d.Issue} field={d.Field}: {d.Description}".Trim()));
        var message = string.IsNullOrWhiteSpace(details)
            ? error.Message
            : $"{error.Message} ({details})";
        if (!string.IsNullOrWhiteSpace(error.DebugId))
        {
            message = $"{message} DebugId={error.DebugId}";
        }

        return new PaymentGatewayException(message, fallbackStatus, error.DebugId);
    }

    public static PaymentGatewayException FromError1(Error1 error, int fallbackStatus)
    {
        var details = error.Details is null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => $"{d.Issue} field={d.Field}: {d.Description}".Trim()));
        var message = string.IsNullOrWhiteSpace(details)
            ? error.Message
            : $"{error.Message} ({details})";
        if (!string.IsNullOrWhiteSpace(error.DebugId))
        {
            message = $"{message} DebugId={error.DebugId}";
        }

        return new PaymentGatewayException(message, fallbackStatus, error.DebugId);
    }

    public static PaymentGatewayException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        if (status < 400)
        {
            status = 502;
        }

        return new PaymentGatewayException("PayPal rejected the request.", status);
    }

    public static PaymentGatewayException FromStatus(HttpStatusCode? status, bool isErrorPath)
    {
        if (isErrorPath && status is HttpStatusCode code && (int)code >= 400)
        {
            return new PaymentGatewayException("PayPal rejected the request.", (int)code);
        }

        return new PaymentGatewayException("The payment provider returned a response that could not be processed.", 502);
    }
}
