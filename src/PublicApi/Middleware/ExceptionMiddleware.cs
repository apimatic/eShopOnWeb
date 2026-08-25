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

    private static int StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => (int)HttpStatusCode.Conflict,
        ResourceNotFoundException => (int)HttpStatusCode.NotFound,
        InvalidOrderStateException => (int)HttpStatusCode.Conflict,
        AuthorizationNotRenewableException => (int)HttpStatusCode.Conflict,
        AuthorizationExpiredException => (int)HttpStatusCode.Conflict,
        PaymentActionRequiredException => (int)HttpStatusCode.Conflict,
        PaymentDeclinedException => (int)HttpStatusCode.PaymentRequired,
        PaymentGatewayException => (int)HttpStatusCode.BadGateway,
        _ => (int)HttpStatusCode.InternalServerError
    };

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodeFor(exception);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}
