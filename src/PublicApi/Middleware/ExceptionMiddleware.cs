using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

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

        var (status, message) = exception switch
        {
            UnusableContactNumberException e => ((int)HttpStatusCode.BadRequest, e.Message),
            CatalogItemNotFoundException e => ((int)HttpStatusCode.BadRequest, e.Message),
            ArgumentException e => ((int)HttpStatusCode.BadRequest, e.Message),
            DuplicateException e => ((int)HttpStatusCode.Conflict, e.Message),
            OrderTransitionException e => ((int)HttpStatusCode.Conflict, e.Message),
            ContactNumberNotFoundException e => ((int)HttpStatusCode.NotFound, e.Message),
            OrderNotFoundException e => ((int)HttpStatusCode.NotFound, e.Message),
            NotificationNotFoundException e => ((int)HttpStatusCode.NotFound, e.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "The caller is not authenticated."),
            SmsProviderException e when e.Kind == SmsProviderFailureKind.CallerRejected
                => ((int)HttpStatusCode.BadRequest, e.Message),
            SmsProviderException e when e.Kind == SmsProviderFailureKind.RateLimited
                => ((int)HttpStatusCode.ServiceUnavailable, "Temporarily unavailable."),
            SmsProviderException => ((int)HttpStatusCode.BadGateway, "Provider unavailable."),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = status,
            Message = message
        }.ToString());
    }
}
