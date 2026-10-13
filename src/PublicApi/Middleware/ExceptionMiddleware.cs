using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Walmart;

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
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            case WalmartIntegrationException walmart:
                return MapWalmart(walmart);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static (int StatusCode, string Message) MapWalmart(WalmartIntegrationException exception)
    {
        var status = (int?)exception.StatusCode;
        return status switch
        {
            // Our credentials / our quota — the caller did nothing wrong and cannot fix it.
            401 or 403 => ((int)HttpStatusCode.BadGateway, "Walmart is currently unavailable."),
            429 => ((int)HttpStatusCode.ServiceUnavailable, "Walmart is temporarily unavailable."),
            // Walmart rejected the request (e.g. an unknown SKU) — surface the same class so the operator can act.
            >= 400 and < 500 => (status.Value, exception.Message),
            // Transport, timeout, 5xx, or unknown outcome — no meaningful caller status.
            _ => ((int)HttpStatusCode.BadGateway, exception.Message),
        };
    }
}
