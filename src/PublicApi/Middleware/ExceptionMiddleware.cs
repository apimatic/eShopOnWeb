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

    // One boundary: convert every failure kind into a coherent, distinct, leak-free caller status.
    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException dup:
                return ((int)HttpStatusCode.Conflict, dup.Message);

            // Caller-actionable application failures.
            case PaymentOperationException op:
                var opStatus = op.Kind switch
                {
                    PaymentErrorKind.NotFound => HttpStatusCode.NotFound,
                    PaymentErrorKind.Conflict => HttpStatusCode.Conflict,
                    _ => HttpStatusCode.UnprocessableEntity
                };
                return ((int)opStatus, op.Message);

            // PayPal answered with a browser-approval challenge — not supported by this integration.
            case PaymentApprovalRequiredException approval:
                return ((int)HttpStatusCode.UnprocessableEntity, approval.Message);

            // A stale hold that can no longer be renewed — operator-actionable.
            case AuthorizationNotRenewableException notRenewable:
                return ((int)HttpStatusCode.Conflict, notRenewable.Message);

            // Any other PayPal-side failure: pass a caller-fixable 4xx through; otherwise report a gateway error.
            case PaymentGatewayException gateway:
                if (gateway.CallerFault && gateway.StatusCode is >= 400 and < 500)
                {
                    return (gateway.StatusCode.Value, gateway.Message);
                }
                return ((int)HttpStatusCode.BadGateway, gateway.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}
