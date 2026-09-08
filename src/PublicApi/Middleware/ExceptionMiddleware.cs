using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

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

        (int statusCode, string message) = exception switch
        {
            // Storefront-level conflicts (existing convention).
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),

            // The requested plan is not offered by the configured product family.
            SubscriptionPlanNotFoundException planNotFound => ((int)HttpStatusCode.NotFound, planNotFound.Message),

            // The token is valid but the shopper record is gone.
            ShopperNotFoundException shopperNotFound => ((int)HttpStatusCode.Unauthorized, shopperNotFound.Message),

            // The Maxio integration is not configured for this deployment.
            MaxioConfigurationException configurationException => ((int)HttpStatusCode.InternalServerError, configurationException.Message),

            // Maxio refused the request. Surface 4xx as-is; convert transient 5xx/429 to 502.
            MaxioApiException apiException when IsServerSide(apiException.StatusCode) => ((int)HttpStatusCode.BadGateway, apiException.Message),
            MaxioApiException apiException => ((int)apiException.StatusCode, apiException.Message),

            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static bool IsServerSide(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return code >= 500 || statusCode == HttpStatusCode.TooManyRequests;
    }
}
