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

        // Map domain/integration failures onto the status an API caller can act on.
        var (statusCode, message) = exception switch
        {
            NotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            DuplicateException duplicate => (HttpStatusCode.Conflict, duplicate.Message),
            AuthorizationNotRenewableException notRenewable => (HttpStatusCode.Conflict, notRenewable.Message),
            InvalidOperationException invalid => (HttpStatusCode.Conflict, invalid.Message),
            ArgumentException argument => (HttpStatusCode.BadRequest, argument.Message),
            // PayPal rejected/failed the request — surface it as a bad gateway, not a server bug.
            PayPalGatewayException gateway => (HttpStatusCode.BadGateway, gateway.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
