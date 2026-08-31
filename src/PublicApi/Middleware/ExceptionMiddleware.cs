using System;
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
            _logger.LogError(ex, "Request {method} {path} failed", httpContext.Request.Method, httpContext.Request.Path);
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        switch (exception)
        {
            case DuplicateException:
            case PaymentDomainException:
            case PaymentRequiresActionException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                break;
            case NotFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                break;
            case ArgumentException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                break;
            case PayPalApiException payPalException:
                // PayPal 4xx means the request was rejected; 5xx/network means PayPal is at fault.
                context.Response.StatusCode = payPalException.StatusCode is >= 400 and < 500
                    ? payPalException.StatusCode
                    : (int)HttpStatusCode.BadGateway;
                break;
            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                break;
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}
