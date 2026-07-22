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
        context.Response.StatusCode = (int)StatusCodeFor(exception);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = MessageFor(exception)
        }.ToString());
    }

    private static HttpStatusCode StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // The caller named something the provider does not have.
        BillingEntityNotFoundException => HttpStatusCode.NotFound,

        // The caller asked for something the provider refused: bad input or an illegal transition.
        BillingValidationException validation when validation.StatusCode == 409 => HttpStatusCode.Conflict,
        BillingValidationException => HttpStatusCode.BadRequest,

        // Upstream problems are ours, not the caller's - never echo the provider's own status.
        BillingAuthenticationException => HttpStatusCode.BadGateway,
        BillingProviderUnavailableException => HttpStatusCode.ServiceUnavailable,
        BillingProviderException => HttpStatusCode.BadGateway,

        // A misconfigured integration is a server fault, not a client one.
        BillingConfigurationException => HttpStatusCode.InternalServerError,

        ArgumentException => HttpStatusCode.BadRequest,

        _ => HttpStatusCode.InternalServerError
    };

    /// <summary>
    /// Surfaces what the caller can act on and nothing else - no stack traces, no request details,
    /// and never a credential.
    /// </summary>
    private static string MessageFor(Exception exception) => exception switch
    {
        DuplicateException or BillingProviderException or BillingConfigurationException or ArgumentException
            => exception.Message,
        _ => "An unexpected error occurred."
    };
}
