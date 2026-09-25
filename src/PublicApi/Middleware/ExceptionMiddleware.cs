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

        var (status, message) = Map(exception);
        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = status,
            Message = message
        }.ToString());
    }

    private static (int Status, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case OrderNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            case InvalidOrderPaymentStateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case UnauthorizedAccessException:
                return ((int)HttpStatusCode.Unauthorized, "Not authorized.");

            case PaymentGatewayException gateway:
                // An operator-actionable condition (e.g. a hold that can no longer be renewed) is a
                // conflict the caller/operator must resolve, not a transient server error.
                if (gateway.OperatorActionable)
                    return ((int)HttpStatusCode.Conflict, gateway.Message);
                // Our credentials/quota failing is not the caller's fault → surface as 502/503.
                if (gateway.StatusCode is 401 or 403)
                    return ((int)HttpStatusCode.BadGateway, "Payment provider is unavailable.");
                if (gateway.StatusCode is 429)
                    return ((int)HttpStatusCode.ServiceUnavailable, "Payment provider is temporarily unavailable.");
                // PayPal rejected the caller's request → hand back the same class of status.
                if (gateway.StatusCode is >= 400 and < 500)
                    return (gateway.StatusCode.Value, gateway.Message);
                // Transport failure, timeout, unknown outcome, or provider 5xx.
                return ((int)HttpStatusCode.BadGateway, gateway.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}
