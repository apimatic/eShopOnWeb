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

        var (statusCode, message) = Describe(exception);

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Chooses the status and the caller-facing message. Only exceptions this application raises
    /// deliberately have their message echoed; anything unexpected is reported generically so internal
    /// detail never reaches a caller.
    /// </summary>
    private static (int StatusCode, string Message) Describe(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case SubscriptionNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            case InvalidSubscriptionOperationException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            // Guard-clause failures are caller input problems, not server faults.
            case ArgumentException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            // A wrong or missing billing configuration is an operator problem, not a caller problem.
            case BillingConfigurationException:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);

            case BillingProviderException billingProviderException:
                return (MapProviderStatus(billingProviderException.StatusCode), exception.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Translates the billing provider's status into one that means the same thing to our caller.
    /// </summary>
    private static int MapProviderStatus(int providerStatusCode) => providerStatusCode switch
    {
        (int)HttpStatusCode.NotFound => (int)HttpStatusCode.NotFound,
        (int)HttpStatusCode.TooManyRequests => (int)HttpStatusCode.TooManyRequests,
        (int)HttpStatusCode.ServiceUnavailable => (int)HttpStatusCode.ServiceUnavailable,
        (int)HttpStatusCode.RequestTimeout => (int)HttpStatusCode.GatewayTimeout,

        // The caller sent something the provider would not accept.
        (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.UnprocessableEntity
            => (int)HttpStatusCode.BadRequest,

        // Anything else — including our own credentials being rejected — is an upstream failure.
        _ => (int)HttpStatusCode.BadGateway
    };
}
