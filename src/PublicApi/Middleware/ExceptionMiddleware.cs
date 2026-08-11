using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using System;

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

        var statusCode = exception switch
        {
            DuplicateException => HttpStatusCode.Conflict,
            OrderNotFoundException => HttpStatusCode.NotFound,
            // The card needs a browser approval we intentionally do not do, or the authorization can
            // no longer be renewed — both are actionable conflicts for the caller/operator.
            PaymentChallengeRequiredException => HttpStatusCode.Conflict,
            AuthorizationNotRenewableException => HttpStatusCode.Conflict,
            // Upstream PayPal failure (declines, gateway errors).
            PaymentGatewayException => HttpStatusCode.BadGateway,
            // Bad input or an illegal state transition requested by the caller.
            ArgumentException => HttpStatusCode.BadRequest,
            InvalidOperationException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}
