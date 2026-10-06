using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;
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

        switch (exception)
        {
            case DuplicateException duplicationException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = duplicationException.Message
                }.ToString());
                break;

            case MaxioApiException maxioException:
                context.Response.StatusCode = MaxioStatusCode(maxioException);
                var maxioMessage = maxioException.ErrorMessages.Count > 0
                    ? string.Join("; ", maxioException.ErrorMessages)
                    : maxioException.Message;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = maxioMessage
                }.ToString());
                break;

            case ArgumentException argumentException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = argumentException.Message
                }.ToString());
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.Response.WriteAsync(new ErrorDetails()
                {
                    StatusCode = context.Response.StatusCode,
                    Message = exception.Message
                }.ToString());
                break;
        }
    }

    private static int MaxioStatusCode(MaxioApiException exception)
    {
        if (exception.IsNotFound)
        {
            return (int)HttpStatusCode.NotFound;
        }

        if (exception.IsUnprocessableEntity)
        {
            return (int)HttpStatusCode.UnprocessableEntity;
        }

        return (int)HttpStatusCode.BadGateway;
    }
}
