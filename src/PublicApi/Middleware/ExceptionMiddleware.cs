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

        var (statusCode, message) = exception switch
        {
            DuplicateException ex => ((int)HttpStatusCode.Conflict, ex.Message),
            OrderTransitionException ex => ((int)HttpStatusCode.Conflict, ex.Message),
            InvalidContactNumberException ex => ((int)HttpStatusCode.BadRequest, ex.Message),
            CatalogItemUnavailableException ex => ((int)HttpStatusCode.BadRequest, ex.Message),
            NotificationNotEligibleException ex => ((int)HttpStatusCode.BadRequest, ex.Message),
            OrderNotFoundException ex => ((int)HttpStatusCode.NotFound, ex.Message),
            NotificationNotFoundException ex => ((int)HttpStatusCode.NotFound, ex.Message),
            ContactNumberNotFoundException ex => ((int)HttpStatusCode.NotFound, ex.Message),
            SmsProviderException ex when ex.StatusCode is 401 or 403 => (502, "Provider unavailable."),
            SmsProviderException ex when ex.StatusCode is 429 => (503, "Temporarily unavailable."),
            SmsProviderException ex when ex.StatusCode is >= 400 and < 500 => (ex.StatusCode.Value, ex.Message),
            SmsProviderException ex => (502, ex.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}
