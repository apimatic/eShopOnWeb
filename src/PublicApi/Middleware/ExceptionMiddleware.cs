using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        var (statusCode, message, errors) = Translate(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Request {Path} failed with {StatusCode}.", context.Request.Path, statusCode);
        }
        else
        {
            _logger.LogWarning("Request {Path} rejected with {StatusCode}: {Message}", context.Request.Path, statusCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message,
            Errors = errors
        }.ToString());
    }

    /// <summary>
    /// Maps an exception onto the status code and the wording a caller should see.
    /// </summary>
    /// <remarks>
    /// Billing failures are separated by whose problem they are. A plan the caller asked for that
    /// does not exist is a 404; a payload the provider rejected is a 422; a credential or outage
    /// problem is the deployment's fault, not the caller's, and comes back as 502/503/504 so that
    /// clients retry rather than "fix" a request that was already correct.
    /// </remarks>
    private static (int StatusCode, string Message, IReadOnlyList<string>? Errors) Translate(Exception exception) =>
        exception switch
        {
            DuplicateException duplicate =>
                ((int)HttpStatusCode.Conflict, duplicate.Message, null),

            SubscriptionPlanNotFoundException planNotFound =>
                ((int)HttpStatusCode.NotFound, planNotFound.Message, null),

            BillingConfigurationException configuration =>
                ((int)HttpStatusCode.ServiceUnavailable, configuration.Message, null),

            BillingProviderException provider =>
                (TranslateProviderStatus(provider), provider.Message,
                    provider.ProviderErrors.Count > 0 ? provider.ProviderErrors : null),

            _ => ((int)HttpStatusCode.InternalServerError, exception.Message, null)
        };

    private static int TranslateProviderStatus(BillingProviderException exception) => exception.ProviderStatusCode switch
    {
        400 => (int)HttpStatusCode.BadRequest,
        404 => (int)HttpStatusCode.NotFound,
        422 => (int)HttpStatusCode.UnprocessableEntity,
        429 => (int)HttpStatusCode.ServiceUnavailable,
        504 => (int)HttpStatusCode.GatewayTimeout,
        _ => (int)HttpStatusCode.BadGateway
    };
}
