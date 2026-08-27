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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message),
        EntityNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
        BadRequestException badRequest => ((int)HttpStatusCode.BadRequest, badRequest.Message),
        OrderStateException orderState => ((int)HttpStatusCode.Conflict, orderState.Message),

        // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
        SmsProviderException provider when (int?)provider.ProviderStatusCode is 401 or 403 =>
            ((int)HttpStatusCode.BadGateway, "The messaging provider is unavailable."),
        SmsProviderException provider when (int?)provider.ProviderStatusCode is 429 =>
            ((int)HttpStatusCode.ServiceUnavailable, "The messaging provider is temporarily unavailable."),

        // The provider rejected the caller's request — hand back the same status so they can act on it.
        SmsProviderException provider when (int?)provider.ProviderStatusCode is >= 400 and < 500 =>
            ((int)provider.ProviderStatusCode!, provider.Message),

        // Transport, timeout, provider 5xx — no meaningful caller status.
        SmsProviderException provider => ((int)HttpStatusCode.BadGateway, provider.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
