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

    // Maps expected domain/gateway failures to the right HTTP status. Every branch surfaces only a
    // caller-safe message; an unexpected exception falls through to a generic 500.
    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
        OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        SavedPaymentMethodNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        InvalidPaymentRequestException => ((int)HttpStatusCode.BadRequest, exception.Message),
        PaymentStateException => ((int)HttpStatusCode.Conflict, exception.Message),
        // A PayPal 4xx the caller can act on (e.g. a declined card) surfaces as that client status;
        // a transport/parse/5xx failure has no meaningful client status, so it becomes 502.
        PaymentGatewayException gatewayException => (
            gatewayException.IsClientError ? gatewayException.ProviderStatusCode!.Value : (int)HttpStatusCode.BadGateway,
            gatewayException.Message),
        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
