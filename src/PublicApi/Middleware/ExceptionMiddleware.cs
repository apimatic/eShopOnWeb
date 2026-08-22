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

        var (status, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalid => ((int)HttpStatusCode.BadRequest, invalid.Message),
            EmptyCatalogOrderException empty => ((int)HttpStatusCode.BadRequest, empty.Message),
            CatalogItemNotFoundException missingItem => ((int)HttpStatusCode.BadRequest, missingItem.Message),
            ContactNumberNotFoundException => ((int)HttpStatusCode.NotFound, "Contact number was not found."),
            OrderNotFoundException => ((int)HttpStatusCode.NotFound, "Order was not found."),
            NotificationNotFoundException => ((int)HttpStatusCode.NotFound, "Notification was not found."),
            InvalidOrderStateException state => ((int)HttpStatusCode.Conflict, state.Message),
            NotificationResendNotAllowedException resend => ((int)HttpStatusCode.Conflict, resend.Message),
            SmsGatewayException sms => MapSmsGateway(sms),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = status,
            Message = message
        }.ToString());
    }

    private static (int Status, string Message) MapSmsGateway(SmsGatewayException exception) =>
        exception.HttpStatusCode switch
        {
            401 or 403 => (502, "The messaging provider is unavailable."),
            429 => (503, "The messaging provider is temporarily unavailable."),
            >= 400 and < 500 => (exception.HttpStatusCode.Value, exception.Message),
            _ => (502, exception.Message)
        };
}
