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
        if (exception is MaxioApiException maxioException)
        {
            statusCode = maxioException.StatusCode >= 400 && maxioException.StatusCode <= 599
                ? maxioException.StatusCode
                : (int)HttpStatusCode.BadGateway;
        }
        else if (exception is ApiException apiException)
        {
            statusCode = apiException.StatusCode >= 400 && apiException.StatusCode <= 599
                ? apiException.StatusCode
                : (int)HttpStatusCode.InternalServerError;
        }
        else if (exception is DuplicateException)
        {
            statusCode = (int)HttpStatusCode.Conflict;
        }
        else
        {
            statusCode = (int)HttpStatusCode.InternalServerError;
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = exception.Message
        }.ToString());
    }
}
