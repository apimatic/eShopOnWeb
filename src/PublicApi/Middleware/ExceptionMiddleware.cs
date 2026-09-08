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

        int statusCode;
        string message;

        switch (exception)
        {
            case DuplicateException duplicationException:
                statusCode = (int)HttpStatusCode.Conflict;
                message = duplicationException.Message;
                break;

            case SubscriptionPlanNotFoundException planNotFoundException:
                statusCode = (int)HttpStatusCode.NotFound;
                message = planNotFoundException.Message;
                break;

            case MaxioConfigurationException configurationException:
                statusCode = (int)HttpStatusCode.ServiceUnavailable;
                message = configurationException.Message;
                break;

            case MaxioApiException apiException:
                statusCode = (int)HttpStatusCode.BadGateway;
                message = apiException.Message;
                break;

            default:
                statusCode = (int)HttpStatusCode.InternalServerError;
                message = exception.Message;
                break;
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

