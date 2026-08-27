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

        _logger.LogError(exception, "An API request failed with {ExceptionType}.", exception.GetType().Name);

        if (exception is SubscriptionRequestException requestException)
        {
            context.Response.StatusCode = (int)requestException.StatusCode;
            await WriteErrorAsync(context, requestException.Message);
        }
        else if (exception is MaxioBillingException maxioException)
        {
            context.Response.StatusCode = (int)MapMaxioStatus(maxioException);
            await WriteErrorAsync(context, maxioException.Message);
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, duplicationException.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorAsync(context, "An unexpected server error occurred.");
        }
    }

    private static HttpStatusCode MapMaxioStatus(MaxioBillingException exception)
    {
        if (exception.OutcomeMayBeAmbiguous)
        {
            return HttpStatusCode.ServiceUnavailable;
        }

        return exception.ProviderStatusCode switch
        {
            HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound => HttpStatusCode.NotFound,
            HttpStatusCode.Conflict => HttpStatusCode.Conflict,
            HttpStatusCode.UnprocessableEntity => HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.TooManyRequests => HttpStatusCode.TooManyRequests,
            _ => HttpStatusCode.BadGateway,
        };
    }

    private static Task WriteErrorAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = message
            }.ToString());
}
