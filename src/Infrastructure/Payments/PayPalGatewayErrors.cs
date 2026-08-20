using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalGatewayErrors
{
    public static void Throw(SdkException<CreateOrderError> ex) =>
        ThrowTyped(ex.Error.TryGetError, ex.Error.TryGetRawError);

    public static void Throw(SdkException<AuthorizeOrderError> ex) =>
        ThrowTyped(ex.Error.TryGetError, ex.Error.TryGetRawError);

    public static void Throw(SdkException<GetOrderError> ex) =>
        ThrowTyped(ex.Error.TryGetError, ex.Error.TryGetRawError);

    public static void Throw(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            throw FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            throw FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    public static void Throw(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            throw FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            throw FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    public static void Throw(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            throw FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            throw FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    public static void Throw(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            throw FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            throw FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    public static void Throw(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            throw FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            throw FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    public static void Throw(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            throw FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            throw FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    public static void Throw(SdkException<GetRefundError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            throw FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            throw FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    public static void Throw(SdkException<CreatePaymentTokenError> ex) =>
        ThrowVault(ex.Error.TryGetError1, ex.Error.TryGetRawError);

    public static void Throw(SdkException<ListCustomerPaymentTokensError> ex) =>
        ThrowVault(ex.Error.TryGetError1, ex.Error.TryGetRawError);

    public static void Throw(SdkException<GetPaymentTokenError> ex) =>
        ThrowVault(ex.Error.TryGetError1, ex.Error.TryGetRawError);

    public static void Throw(SdkException<DeletePaymentTokenError> ex) =>
        ThrowVault(ex.Error.TryGetError1, ex.Error.TryGetRawError);

    public static void Throw(SdkException<RawError> ex) => throw FromRaw(ex.Error);

    public static PaymentException FromJson(Exception ex)
    {
        var status = PayPalLastStatus.Code.Value;
        if (status is >= 400 and < 500)
        {
            return new PaymentException("The payment processor rejected the request.", status.Value, ex);
        }

        return new PaymentException("The payment processor returned a response that could not be processed.", 502, ex);
    }

    public static PaymentException Unreachable(Exception ex) =>
        new("The payment processor is unreachable.", 502, ex);

    public static PaymentException DuplicateWrite(Exception ex) =>
        new("The payment request may already have reached PayPal. Refresh payment state before retrying.", 409, ex);

    private static void ThrowTyped(
        TryGetError tryGetError,
        TryGetRaw tryGetRaw)
    {
        if (tryGetError(out var error))
        {
            throw FromError(error);
        }

        if (tryGetRaw(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    private static void ThrowVault(TryGetError1 tryGetError1, TryGetRaw tryGetRaw)
    {
        if (tryGetError1(out var error))
        {
            throw FromError1(error);
        }

        if (tryGetRaw(out var raw))
        {
            throw FromRaw(raw);
        }

        throw Unknown();
    }

    private static PaymentException FromError(Error error)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        var description = error.Details?.FirstOrDefault()?.Description;
        var message = MapIssueMessage(issue, description ?? error.Message);
        return new PaymentException(message, StatusFromName(error.Name), error.DebugId, issue);
    }

    private static PaymentException FromError1(Error1 error)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        var description = error.Details?.FirstOrDefault()?.Description;
        var message = MapIssueMessage(issue, description ?? error.Message);
        return new PaymentException(message, StatusFromName(error.Name), error.DebugId, issue);
    }

    private static PaymentException FromRaw(RawError raw)
    {
        _ = raw.ReadAsString();
        return new PaymentException("The payment processor rejected the request.", (int)raw.StatusCode);
    }

    private static string MapIssueMessage(string? issue, string fallback)
    {
        if (string.Equals(issue, "INSTRUMENT_DECLINED", StringComparison.OrdinalIgnoreCase))
        {
            return "The card was declined.";
        }

        if (string.Equals(issue, "PAYMENT_DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return "The payment was denied.";
        }

        if (string.Equals(issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return "The payment authorization has expired.";
        }

        return string.IsNullOrWhiteSpace(fallback) ? "The payment processor rejected the request." : fallback;
    }

    private static int StatusFromName(string? name) => name switch
    {
        "AUTHENTICATION_FAILURE" => 401,
        "NOT_AUTHORIZED" => 403,
        "RESOURCE_NOT_FOUND" => 404,
        "UNPROCESSABLE_ENTITY" => 422,
        "INVALID_REQUEST" => 400,
        "CONFLICT" => 409,
        _ => 400
    };

    private static PaymentException Unknown() =>
        new("The payment processor rejected the request.", 400);

    private delegate bool TryGetError(out Error error);
    private delegate bool TryGetError1(out Error1 error);
    private delegate bool TryGetRaw(out RawError raw);
}
