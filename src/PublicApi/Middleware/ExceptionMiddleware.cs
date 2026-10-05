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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is BillingProviderException billingException)
        {
            context.Response.StatusCode = billingException.Kind switch
            {
                BillingFailureKind.NoResponse => (int)HttpStatusCode.GatewayTimeout,
                BillingFailureKind.Rejected => (int)HttpStatusCode.UnprocessableEntity,
                _ => (int)HttpStatusCode.BadGateway
            };
            var message = billingException.Message;
            if (billingException.Errors.Count > 0)
            {
                message += " " + string.Join(" ", billingException.Errors);
            }
            if (billingException.OutcomeUnknown)
            {
                message += " The request may still complete; check GET api/my-subscriptions shortly.";
            }
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = message
            }.ToString());
        }
        else if (exception is SubscriptionInProgressException or SubscriptionPlanNotFoundException or ShopperNotFoundException)
        {
            context.Response.StatusCode = exception switch
            {
                SubscriptionInProgressException => (int)HttpStatusCode.Conflict,
                SubscriptionPlanNotFoundException => (int)HttpStatusCode.BadRequest,
                _ => (int)HttpStatusCode.Unauthorized
            };
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
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
