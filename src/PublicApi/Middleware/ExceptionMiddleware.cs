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

        switch (exception)
        {
            case DuplicateException duplicationException:
                await WriteErrorAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
                break;
            case SubscriptionPlanNotFoundException planNotFound:
                await WriteErrorAsync(context, HttpStatusCode.NotFound, planNotFound.Message);
                break;
            case MaxioApiException maxioException when maxioException.StatusCode == 409:
                // Maxio treats some conflicts (e.g. duplicate customer reference) as 422; a 409 is rare.
                await WriteErrorAsync(context, HttpStatusCode.Conflict, maxioException.Message);
                break;
            case MaxioApiException maxioException:
                // The billing provider rejected/failed the request; this is a gateway problem, not a bug here.
                await WriteErrorAsync(context, HttpStatusCode.BadGateway, maxioException.Message);
                break;
            default:
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
