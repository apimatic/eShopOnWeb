using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
// PaymentGatewayException, PaymentNotFoundException, PaymentStateException,
// PaymentApprovalRequiredException all live in ApplicationCore.Exceptions.

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

    // One caller-facing ladder from failure kind to HTTP status. The message is always caller-safe
    // (built at the gateway boundary for provider failures); SDK/framework detail never reaches the wire.
    private static (int Status, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
        PaymentNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        PaymentStateException => ((int)HttpStatusCode.BadRequest, exception.Message),
        // A browser challenge is something the caller cannot resolve through this API.
        PaymentApprovalRequiredException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),
        PaymentGatewayException gateway => MapGateway(gateway),
        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static (int Status, string Message) MapGateway(PaymentGatewayException ex)
    {
        var code = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
        // Our credentials / our quota / provider 5xx: the caller did nothing wrong and cannot fix it.
        if (code is 401 or 403 or 429 or >= 500)
            return ((int)HttpStatusCode.BadGateway, ex.Message);
        // The provider rejected the caller's request — hand back an actionable client status.
        if (code is >= 400 and < 500)
            return (code, ex.Message);
        // Transport failure / unknown.
        return ((int)HttpStatusCode.BadGateway, ex.Message);
    }
}
