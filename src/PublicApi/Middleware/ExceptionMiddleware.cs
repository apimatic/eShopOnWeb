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

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException dup => ((int)HttpStatusCode.Conflict, dup.Message),

        // Payment domain errors carry the status a caller should see.
        PaymentException pe => (pe.StatusCode, pe.Message),

        // Provider failures: our-fault/unknown -> 502; the caller's rejected request -> its own 4xx.
        PaymentGatewayException ge => (MapGateway(ge), ge.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static int MapGateway(PaymentGatewayException ge) => ge.Kind switch
    {
        PaymentGatewayFailureKind.ProviderUnavailable => (int)HttpStatusCode.BadGateway,
        PaymentGatewayFailureKind.Unreadable => (int)HttpStatusCode.BadGateway,
        PaymentGatewayFailureKind.RequestRejected => ge.ProviderStatus is HttpStatusCode s
            && (int)s >= 400 && (int)s < 500
                ? (int)s
                : (int)HttpStatusCode.BadRequest,
        _ => (int)HttpStatusCode.BadGateway
    };
}
