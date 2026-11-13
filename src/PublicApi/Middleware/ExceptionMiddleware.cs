using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

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

        if (exception is BillingException or UserNotFoundException)
        {
            await WriteBillingErrorAsync(context, exception);
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

    /// <summary>
    /// Subscription-billing failures: each kind keeps a distinct status. Messages are the exceptions' own
    /// caller-safe texts — never provider payloads, URLs or SDK type names.
    /// </summary>
    private static async Task WriteBillingErrorAsync(HttpContext context, Exception exception)
    {
        var (status, message) = exception switch
        {
            SubscriptionPlanNotOfferedException e => (HttpStatusCode.BadRequest, e.Message),
            SubscriptionRequestInProgressException e => (HttpStatusCode.Conflict, e.Message),
            BillingRequestRejectedException e => (HttpStatusCode.UnprocessableEntity,
                e.Errors.Count == 0 ? e.Message : $"{e.Message} {string.Join(" ", e.Errors)}"),
            BillingUnavailableException e => (HttpStatusCode.ServiceUnavailable, e.Message),
            BillingIntegrationException e => (HttpStatusCode.BadGateway, e.Message),
            UserNotFoundException => (HttpStatusCode.Unauthorized, "The authenticated user was not found."),
            _ => (HttpStatusCode.InternalServerError, "Unexpected billing error.")
        };

        context.Response.StatusCode = (int)status;
        if (status == HttpStatusCode.ServiceUnavailable)
        {
            context.Response.Headers.RetryAfter = "5";
        }

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
