using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

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

    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case PaymentNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            case InvalidPaymentOperationException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            // PaymentApprovalRequiredException is a PaymentGatewayException with StatusCode 402; both are
            // handled here. A provider client-error (4xx) is surfaced as-is; anything else as its carried
            // status (defaulting to 502 Bad Gateway) so a caller can tell "you sent something invalid" from
            // "the provider had a problem". The message is already caller-safe (no SDK internals).
            case PaymentGatewayException gatewayException:
                var status = gatewayException.StatusCode is >= 400 and < 600
                    ? gatewayException.StatusCode!.Value
                    : (int)HttpStatusCode.BadGateway;
                return (status, gatewayException.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}
