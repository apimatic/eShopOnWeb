using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalExceptionMapper
{
    public static PayPalProviderException FromCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 400);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal rejected the authorization request.");
    }

    public static PayPalProviderException FromGetOrder(SdkException<GetOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 404);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not return the order.");
    }

    public static PayPalProviderException FromGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not return the authorization.");
    }

    public static PayPalProviderException FromReauthorizePayment(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 422);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not renew the authorization.");
    }

    public static PayPalProviderException FromCaptureAuthorizedPayment(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 400);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not capture the authorization.");
    }

    public static PayPalProviderException FromGetCapturedPayment(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not return the capture.");
    }

    public static PayPalProviderException FromVoidPayment(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 409);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not release the authorization.");
    }

    public static PayPalProviderException FromRefundCapturedPayment(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 400);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not refund the capture.");
    }

    public static PayPalProviderException FromGetRefund(SdkException<GetRefundError> ex)
    {
        if (ex.Error.TryGetError(out Error error))
        {
            return FromError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not return the refund.");
    }

    public static PayPalProviderException FromCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 error))
        {
            return FromError1(error, 400);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not save the card.");
    }

    public static PayPalProviderException FromListCustomerPaymentTokens(SdkException<ListCustomerPaymentTokensError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 error))
        {
            return FromError1(error, 400);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not list saved cards.");
    }

    public static PayPalProviderException FromGetPaymentToken(SdkException<GetPaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 error))
        {
            return FromError1(error, 404);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not return the saved card.");
    }

    public static PayPalProviderException FromDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 error))
        {
            return FromError1(error, 400);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return Unknown("PayPal could not delete the saved card.");
    }

    public static PayPalProviderException FromSearchTransactions(SdkException<RawError> ex)
    {
        return FromRaw(ex.Error);
    }

    public static PayPalProviderException FromJson(JsonException ex)
    {
        var status = PayPalStatusCaptureHandler.LastStatus;
        if (status is HttpStatusCode code && (int)code >= 400)
        {
            return new PayPalProviderException(
                "PayPal rejected the request.",
                (int)code,
                inner: ex);
        }

        return new PayPalProviderException(
            "PayPal returned a response that could not be processed.",
            502,
            inner: ex);
    }

    public static PayPalProviderException Unreachable(Exception ex)
    {
        return new PayPalProviderException("PayPal is unreachable. Try again shortly.", 503, inner: ex);
    }

    private static PayPalProviderException FromError(Error error, int fallbackStatus)
    {
        var issues = error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i));
        return new PayPalProviderException(
            BuildMessage(error.Name, error.Message, issues),
            MapStatus(error.Name, fallbackStatus),
            error.DebugId);
    }

    private static PayPalProviderException FromError1(Error1 error, int fallbackStatus)
    {
        var issues = error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i));
        return new PayPalProviderException(
            BuildMessage(error.Name, error.Message, issues),
            MapStatus(error.Name, fallbackStatus),
            error.DebugId);
    }

    private static PayPalProviderException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        if (status < 400)
        {
            status = 502;
        }

        var body = SafeBody(raw);
        var message = string.IsNullOrWhiteSpace(body)
            ? "PayPal returned an error."
            : "PayPal returned an error.";

        return new PayPalProviderException(message, status);
    }

    private static string SafeBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildMessage(string? name, string? message, IEnumerable<string?>? issues)
    {
        var issueText = issues is null ? null : string.Join(", ", issues.Where(i => !string.IsNullOrWhiteSpace(i)));
        if (!string.IsNullOrWhiteSpace(issueText) && !string.IsNullOrWhiteSpace(message))
        {
            return $"{message} ({name}: {issueText})";
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            return string.IsNullOrWhiteSpace(name) ? message : $"{message} ({name})";
        }

        return string.IsNullOrWhiteSpace(name) ? "PayPal returned an error." : name!;
    }

    private static int MapStatus(string? name, int fallback)
    {
        return name switch
        {
            "AUTHENTICATION_FAILURE" or "NOT_AUTHORIZED" => 401,
            "PERMISSION_DENIED" or "NOT_AUTHORIZED_FOR_THIS_RESOURCE" => 403,
            "RESOURCE_NOT_FOUND" or "INVALID_RESOURCE_ID" => 404,
            "UNPROCESSABLE_ENTITY" or "SEMANTIC_ERROR" => 422,
            "RATE_LIMIT_REACHED" => 429,
            "INTERNAL_SERVER_ERROR" => 502,
            _ => fallback
        };
    }

    private static PayPalProviderException Unknown(string message) =>
        new(message, 502);
}
