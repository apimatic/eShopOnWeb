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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (status, message) = exception switch
        {
            DuplicateException ex => (HttpStatusCode.Conflict, ex.Message),
            InvalidOperationException ex => (HttpStatusCode.Conflict, ex.Message),
            InvalidContactNumberException ex => (HttpStatusCode.BadRequest, ex.Message),
            ArgumentException ex => (HttpStatusCode.BadRequest, ex.Message),
            ContactNumberNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
            OrderNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
            NotificationNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
            ProviderUnavailableException ex when (int?)ex.StatusCode == 429 => (HttpStatusCode.ServiceUnavailable, "Temporarily unavailable."),
            ProviderUnavailableException => (HttpStatusCode.BadGateway, "Provider unavailable."),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
