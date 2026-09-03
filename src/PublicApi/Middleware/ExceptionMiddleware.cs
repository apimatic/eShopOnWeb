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
            ContactNumberRejectedException rejected => ((int)HttpStatusCode.BadRequest, rejected.Message),
            CatalogOrderException catalog => ((int)HttpStatusCode.BadRequest, catalog.Message),
            ArgumentException argument => ((int)HttpStatusCode.BadRequest, argument.Message),
            EntityNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            OrderStateException state => ((int)HttpStatusCode.Conflict, state.Message),
            NotificationOperationException operation => ((int)HttpStatusCode.Conflict, operation.Message),
            TwilioProviderException provider => (MapProviderStatus(provider.HttpStatusCode), provider.Message),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static int MapProviderStatus(int? status) =>
        status switch
        {
            401 or 403 => (int)HttpStatusCode.BadGateway,
            429 => (int)HttpStatusCode.ServiceUnavailable,
            >= 400 and < 500 => status.Value,
            _ => (int)HttpStatusCode.BadGateway
        };
}
