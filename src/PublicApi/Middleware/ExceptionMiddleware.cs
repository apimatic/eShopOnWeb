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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else
        {
            var (statusCode, message) = ToErrorResponse(exception);

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = statusCode,
                Message = message
            }.ToString());
        }
    }

    private static (int StatusCode, string Message) ToErrorResponse(Exception exception)
    {
        return exception switch
        {
            MaxioConfigurationException => ((int)HttpStatusCode.ServiceUnavailable, exception.Message),
            SubscriptionPlanNotFoundException or SubscriptionRejectedException => ((int)HttpStatusCode.BadRequest, exception.Message),
            AlreadySubscribedException => ((int)HttpStatusCode.Conflict, exception.Message),
            MaxioApiException => ((int)HttpStatusCode.BadGateway, exception.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };
    }
}
