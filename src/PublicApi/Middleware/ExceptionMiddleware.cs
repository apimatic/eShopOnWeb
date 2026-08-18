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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException => (HttpStatusCode.Conflict, duplicationException.Message),
            SubscriptionPlanNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            BillingValidationException validation => (HttpStatusCode.BadRequest, validation.Message),
            MaxioConfigurationException configuration => (HttpStatusCode.ServiceUnavailable, configuration.Message),
            MaxioApiException { StatusCode: 404 } apiNotFound => (HttpStatusCode.NotFound, apiNotFound.Message),
            MaxioApiException { StatusCode: 422 } apiValidation => (HttpStatusCode.BadRequest, apiValidation.Message),
            MaxioApiException api => (HttpStatusCode.BadGateway, api.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
