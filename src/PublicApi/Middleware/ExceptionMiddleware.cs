using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.WalmartIntegration;

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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);
            case InsufficientStockException:
            case CatalogItemNotFoundException:
            case EmptyBasketOnCheckoutException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);
            case WalmartApiException walmart:
                return MapWalmart(walmart);
            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static (int StatusCode, string Message) MapWalmart(WalmartApiException exception)
    {
        // A provider 4xx the caller can act on is passed through; failures that are OUR fault
        // (credentials/quota) or that leave the outcome unknown become 5xx. The caller never sees the
        // SDK exception type or a secret — only this caller-safe message.
        var status = exception.StatusCode;
        if (status == HttpStatusCode.NotFound)
        {
            return ((int)HttpStatusCode.NotFound, exception.Message);
        }
        if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
        {
            return ((int)HttpStatusCode.BadGateway, "Walmart Marketplace is currently unavailable.");
        }
        if (status == (HttpStatusCode)429)
        {
            return ((int)HttpStatusCode.ServiceUnavailable, "Walmart Marketplace is temporarily unavailable.");
        }
        if (status.HasValue && (int)status.Value >= 400 && (int)status.Value < 500)
        {
            return ((int)status.Value, exception.Message);
        }
        return ((int)HttpStatusCode.BadGateway, exception.Message);
    }
}
