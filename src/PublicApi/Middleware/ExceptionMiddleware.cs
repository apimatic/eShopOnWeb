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
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalidContact => ((int)HttpStatusCode.BadRequest, invalidContact.Message),
            InvalidOrderStateException invalidState => ((int)HttpStatusCode.Conflict, invalidState.Message),
            CatalogItemNotFoundException catalogMissing => ((int)HttpStatusCode.BadRequest, catalogMissing.Message),
            ContactNumberNotFoundException or OrderNotFoundException or NotificationNotFoundException
                => ((int)HttpStatusCode.NotFound, exception.Message),
            ArgumentException argumentException => ((int)HttpStatusCode.BadRequest, argumentException.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "The caller is not authenticated."),
            TwilioUnavailableException twilioUnavailable => ((int)HttpStatusCode.ServiceUnavailable, twilioUnavailable.Message),
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
