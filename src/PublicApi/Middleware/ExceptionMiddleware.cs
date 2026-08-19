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

        context.Response.StatusCode = exception switch
        {
            DuplicateException => (int)HttpStatusCode.Conflict,
            SubscriptionPlanNotFoundException => (int)HttpStatusCode.NotFound,
            MaxioConfigurationException => (int)HttpStatusCode.ServiceUnavailable,
            MaxioApiException maxio => MapMaxioStatus(maxio.StatusCode),
            ArgumentException => (int)HttpStatusCode.BadRequest,
            _ => (int)HttpStatusCode.InternalServerError
        };

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    private static int MapMaxioStatus(int statusCode) =>
        statusCode switch
        {
            400 or 401 or 403 or 404 or 409 or 422 or 429 => statusCode == 422 ? 400 : statusCode,
            _ => (int)HttpStatusCode.BadGateway
        };
}
