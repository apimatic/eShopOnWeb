using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

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

        var (statusCode, message) = Describe(exception);
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) Describe(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),

        // The caller asked for a plan that does not exist in the configured catalog.
        SubscriptionPlanNotFoundException => (HttpStatusCode.NotFound, exception.Message),

        // The billing system refused the request on its merits, e.g. the plan needs a payment
        // method. Retrying unchanged will not help, so this is the caller's problem to fix.
        BillingValidationException => (HttpStatusCode.UnprocessableEntity, exception.Message),

        BillingConflictException => (HttpStatusCode.Conflict, exception.Message),

        // Throttled, timed out, or down. Safe for the caller to retry later.
        BillingUnavailableException => (HttpStatusCode.ServiceUnavailable, exception.Message),

        // The Maxio section is missing or incomplete; surfacing it plainly beats a generic 500.
        OptionsValidationException => (HttpStatusCode.ServiceUnavailable,
            $"Subscription billing is not configured. {exception.Message}"),

        // Anything else from the billing integration is an upstream failure, not a caller error.
        BillingException => (HttpStatusCode.BadGateway, exception.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
