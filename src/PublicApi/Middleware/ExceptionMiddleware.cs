using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Stax;
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

        int status;
        string message = exception.Message;
        switch (exception)
        {
            case DuplicateException:
                status = (int)HttpStatusCode.Conflict;
                break;
            case ApiException api:
                status = api.StatusCode;
                break;
            case BadHttpRequestException bad:
                status = bad.StatusCode;
                message = "The request is malformed: " + (bad.InnerException?.Message ?? bad.Message);
                break;
            default:
                status = (int)HttpStatusCode.InternalServerError;
                _logger.LogError(exception, "Unhandled exception");
                break;
        }

        if (context.Response.HasStarted) return;
        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = status,
            Message = message
        }.ToString());
    }
}
