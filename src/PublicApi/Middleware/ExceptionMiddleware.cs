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

        var statusCode = ResolveStatusCode(exception);
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = exception.Message
        }.ToString());
    }

    private static int ResolveStatusCode(Exception exception) => exception switch
    {
        DuplicateException => (int)HttpStatusCode.Conflict,
        UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
        EntityNotFoundException => (int)HttpStatusCode.NotFound,
        PaymentStateException => (int)HttpStatusCode.Conflict,
        // Payment-gateway failures carry the provider's status so a caller error stays a 4xx and a
        // provider/transport failure stays a 5xx, instead of collapsing everything to one status.
        PaymentGatewayException gatewayException => NormalizeGatewayStatus(gatewayException.StatusCode),
        _ => (int)HttpStatusCode.InternalServerError
    };

    private static int NormalizeGatewayStatus(int? statusCode)
    {
        if (statusCode is null)
            return (int)HttpStatusCode.BadGateway;

        // A provider 4xx is the caller's problem (surface it as-is); anything else is a 5xx.
        return statusCode is >= 400 and < 500
            ? statusCode.Value
            : (int)HttpStatusCode.BadGateway;
    }
}
