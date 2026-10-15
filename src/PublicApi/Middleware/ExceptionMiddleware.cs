using System;
using System.Net;
using System.Text.Json;
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

        if (exception is SubscriptionBillingException billingException)
        {
            context.Response.StatusCode = billingException.Kind switch
            {
                SubscriptionBillingErrorKind.PlanNotOffered => (int)HttpStatusCode.BadRequest,
                SubscriptionBillingErrorKind.Rejected => (int)HttpStatusCode.UnprocessableEntity,
                SubscriptionBillingErrorKind.AlreadySubscribed => (int)HttpStatusCode.Conflict,
                SubscriptionBillingErrorKind.InProgress => (int)HttpStatusCode.Conflict,
                SubscriptionBillingErrorKind.Unavailable => (int)HttpStatusCode.ServiceUnavailable,
                _ => (int)HttpStatusCode.BadGateway
            };
            var details = new SubscriptionErrorDetails
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message,
                Details = billingException.Details
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(details));
        }
        else if (exception is DuplicateException duplicationException)
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
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }
}
