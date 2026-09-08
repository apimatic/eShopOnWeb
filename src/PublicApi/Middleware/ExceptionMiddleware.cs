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
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            SubscriptionConflictException conflictException => ((int)HttpStatusCode.Conflict, conflictException.Message),
            SubscriptionPlanNotFoundException notFoundException => ((int)HttpStatusCode.NotFound, notFoundException.Message),
            InvalidSubscriptionRequestException invalidRequest => ((int)HttpStatusCode.BadRequest, invalidRequest.Message),
            SubscriptionProviderRejectedException rejected => (ProviderStatus(rejected), rejected.Message),
            MaxioBillingUnavailableException unavailable => ((int)HttpStatusCode.BadGateway, unavailable.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static int ProviderStatus(SubscriptionProviderRejectedException exception)
    {
        var status = exception.ProviderStatusCode;
        return status is >= 400 and <= 499 ? status : (int)HttpStatusCode.BadRequest;
    }
}
