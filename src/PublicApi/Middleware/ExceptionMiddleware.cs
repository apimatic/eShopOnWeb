using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

        switch (exception)
        {
            case SubscriptionPlanNotFoundException:
                await WriteErrorAsync(context, HttpStatusCode.NotFound, exception.Message);
                break;
            case SubscriptionRequestException:
                await WriteErrorAsync(context, HttpStatusCode.BadRequest, exception.Message);
                break;
            case SubscriptionServiceUnavailableException:
                await WriteErrorAsync(context, HttpStatusCode.BadGateway, exception.Message);
                break;
            case MaxioConfigurationException:
                await WriteErrorAsync(context, HttpStatusCode.InternalServerError, exception.Message);
                break;
            case DuplicateException duplicateException:
                await WriteErrorAsync(context, HttpStatusCode.Conflict, duplicateException.Message);
                break;
            default:
                _logger.LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path);
                await WriteErrorAsync(context, HttpStatusCode.InternalServerError, exception.Message);
                break;
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
