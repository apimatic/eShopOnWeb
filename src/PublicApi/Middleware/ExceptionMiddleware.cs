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
        var statusCode = HttpStatusCode.InternalServerError;
        var message = exception.Message;

        switch (exception)
        {
            case DuplicateException:
                statusCode = HttpStatusCode.Conflict;
                break;
            case AlreadySubscribedException:
                statusCode = HttpStatusCode.Conflict;
                break;
            case SubscriptionPlanNotFoundException:
                statusCode = HttpStatusCode.NotFound;
                break;
            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Unauthorized;
                break;
            case MaxioConfigurationException:
                statusCode = HttpStatusCode.InternalServerError;
                break;
            case MaxioApiException maxioException:
                statusCode = maxioException.StatusCode >= 400 && maxioException.StatusCode < 500
                    ? (HttpStatusCode)maxioException.StatusCode
                    : HttpStatusCode.BadGateway;
                break;
        }

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
