using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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

    private static (int statusCode, string message) Map(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // Not found (also used for "belongs to another shopper", to avoid revealing another's data).
        OrderNotFoundException or PaymentMethodNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // A request the caller can act on: wrong order state, or nothing left to refund.
        InvalidOrderStateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // A card challenge that would need a browser approval — surfaced, not worked around.
        PaymentRequiresCustomerActionException => (
            (int)HttpStatusCode.Conflict,
            exception.Message),

        // An authorization that can no longer be renewed — an operator-actionable message.
        PaymentGatewayException { IsOperatorActionable: true } => ((int)HttpStatusCode.Conflict, exception.Message),

        // Other gateway failures (declines, provider errors): the caller's request was passed on, but the
        // provider rejected or could not complete it.
        PaymentGatewayException => ((int)HttpStatusCode.BadGateway, exception.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
