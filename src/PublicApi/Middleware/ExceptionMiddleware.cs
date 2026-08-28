using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (PaymentApiException exception)
        {
            await Write(context, exception.StatusCode, exception.Code, exception.Message, null);
        }
        catch (DuplicateException)
        {
            await Write(context, StatusCodes.Status409Conflict, "duplicate", "The resource already exists.", null);
        }
        catch (SdkException<CreateOrderError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<AuthorizeOrderError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<GetAuthorizedPaymentError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<ReauthorizePaymentError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<GetCapturedPaymentError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<VoidPaymentError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<RefundCapturedPaymentError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<GetRefundError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<CreatePaymentTokenError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<ListCustomerPaymentTokensError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<GetPaymentTokenError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<DeletePaymentTokenError> ex) { await Provider(context, Extract(ex.Error)); }
        catch (SdkException<RawError> ex) { await Provider(context, From(ex.Error)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Write(context, StatusCodes.Status502BadGateway, "paypal_unavailable",
                "PayPal could not be reached. The operation may have taken effect; retry with the same idempotency key.", null);
        }
        catch (JsonException)
        {
            await Write(context, StatusCodes.Status502BadGateway, "paypal_response_invalid",
                "PayPal returned a response that could not be processed.", null);
        }
        catch (Exception)
        {
            await Write(context, StatusCodes.Status500InternalServerError, "internal_error",
                "The request could not be completed.", null);
        }
    }

    private static Task Provider(HttpContext context, ProviderProblem problem) =>
        Write(context, problem.StatusCode, "paypal_error", problem.Message, problem.DebugId);

    private static async Task Write(HttpContext context, int statusCode, string code,
        string message, string? debugId)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { code, message, debugId }));
    }

    private static ProviderProblem Extract(CreateOrderError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(AuthorizeOrderError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(GetAuthorizedPaymentError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetNoContent(out var noContent) ? From(noContent) :
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(ReauthorizePaymentError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetNoContent(out var noContent) ? From(noContent) :
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(CaptureAuthorizedPaymentError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetNoContent(out var noContent) ? From(noContent) :
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(GetCapturedPaymentError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetNoContent(out var noContent) ? From(noContent) :
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(VoidPaymentError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetNoContent(out var noContent) ? From(noContent) :
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(RefundCapturedPaymentError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetNoContent(out var noContent) ? From(noContent) :
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(GetRefundError e) =>
        e.TryGetError(out var body) ? From(body) : e.TryGetNoContent(out var noContent) ? From(noContent) :
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(CreatePaymentTokenError e) =>
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(ListCustomerPaymentTokensError e) =>
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(GetPaymentTokenError e) =>
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();
    private static ProviderProblem Extract(DeletePaymentTokenError e) =>
        e.TryGetRawError(out var raw) ? From(raw) : Unknown();

    private static ProviderProblem From(Error error) => new(
        IsProviderInfrastructure(error.Name) ? StatusCodes.Status502BadGateway : StatusCodes.Status422UnprocessableEntity,
        SafeProviderMessage(error.Name, error.Message), error.DebugId);
    private static ProviderProblem From(RawError raw)
    {
        var status = (int)raw.StatusCode;
        if (status < 400 || status > 599) status = StatusCodes.Status502BadGateway;
        return new ProviderProblem(status, "PayPal rejected the request.", null);
    }
    private static ProviderProblem Unknown() =>
        new(StatusCodes.Status502BadGateway, "PayPal returned an unrecognized error response.", null);

    private static bool IsProviderInfrastructure(string? name) => name != null &&
        (name.Contains("AUTHENTICATION", StringComparison.OrdinalIgnoreCase)
         || name.Contains("INTERNAL", StringComparison.OrdinalIgnoreCase));
    private static string SafeProviderMessage(string? name, string? message)
    {
        var prefix = string.IsNullOrWhiteSpace(name) ? "PayPal rejected the request" : $"PayPal {name}";
        return string.IsNullOrWhiteSpace(message) ? $"{prefix}." : $"{prefix}: {message}";
    }

    private sealed record ProviderProblem(int StatusCode, string Message, string? DebugId);
}
