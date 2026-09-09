using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
// PaymentOperationException / PaymentGatewayException live in ApplicationCore.Exceptions (imported above).

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
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode Status, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException dup:
                return (HttpStatusCode.Conflict, dup.Message);

            case PaymentOperationException op:
                return op.Error switch
                {
                    PaymentOperationError.NotFound => (HttpStatusCode.NotFound, op.Message),
                    PaymentOperationError.Forbidden => (HttpStatusCode.Forbidden, op.Message),
                    PaymentOperationError.Conflict => (HttpStatusCode.Conflict, op.Message),
                    _ => (HttpStatusCode.BadRequest, op.Message)
                };

            case PaymentGatewayException gw:
                return gw.Kind switch
                {
                    // Our credentials / quota, or the provider itself — not the caller's to fix.
                    PaymentGatewayErrorKind.ProviderError => (HttpStatusCode.BadGateway, gw.Message),
                    PaymentGatewayErrorKind.ProviderUnavailable => (HttpStatusCode.ServiceUnavailable, gw.Message),
                    // Caller / operator can act on these.
                    PaymentGatewayErrorKind.NotFound => (HttpStatusCode.NotFound, gw.Message),
                    PaymentGatewayErrorKind.Conflict => (HttpStatusCode.Conflict, gw.Message),
                    PaymentGatewayErrorKind.ChallengeRequired => (HttpStatusCode.Conflict, gw.Message),
                    PaymentGatewayErrorKind.CannotReauthorize => (HttpStatusCode.Conflict, gw.Message),
                    _ => (HttpStatusCode.BadRequest, gw.Message)
                };

            default:
                return (HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}
