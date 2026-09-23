using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);        
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    // One ladder for the whole API. Distinct failures stay distinct; nothing leaks card data (the gateway
    // builds only caller-safe messages), and "our fault" provider failures do not masquerade as the
    // caller's fault.
    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case OrderNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            case PaymentValidationException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            case PaymentChallengeRequiredException:
                // The card needs browser approval we do not support — the caller must act, but cannot here.
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case PayPalGatewayException gateway:
                // A caller-actionable provider 4xx (validation/not-found/conflict) passes through; our own
                // auth/rate-limit problems and transport failures are 502, not the caller's fault.
                var status = gateway.ProviderStatusCode;
                if (status is 400 or 404 or 409 or 422)
                    return (status.Value, gateway.Message);
                return ((int)HttpStatusCode.BadGateway, gateway.Message);

            case OperationCanceledException:
                return (StatusGatewayTimeout, "The payment request timed out.");

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private const int StatusGatewayTimeout = 504;
}
