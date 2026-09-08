using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Maxio;

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

        var (statusCode, message) = MapException(exception);

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) MapException(Exception exception)
    {
        return exception switch
        {
            DuplicateException duplicate => (HttpStatusCode.Conflict, duplicate.Message),
            SubscriptionPlanNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            MaxioRequestRejectedException rejected => (HttpStatusCode.BadRequest, rejected.Message),
            MaxioConfigurationException configuration => (HttpStatusCode.InternalServerError, configuration.Message),
            MaxioUnavailableException unavailable => (HttpStatusCode.BadGateway, unavailable.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };
    }
}
