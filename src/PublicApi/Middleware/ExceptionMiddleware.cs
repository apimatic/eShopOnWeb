using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Banking;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;
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
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // client went away
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
        else if (exception is BankApiException bankError)
        {
            await WriteReasonAsync(context, bankError.Status, bankError.Message);
        }
        else if (exception is PlaidException plaidError)
        {
            var (status, reason) = PlaidErrorMapper.Map(plaidError);
            if ((int)status >= 500)
            {
                _logger.LogWarning(plaidError, "Plaid call failed ({ErrorType}/{ErrorCode})", plaidError.ErrorType, plaidError.ErrorCode);
            }
            await WriteReasonAsync(context, status, reason);
        }
        else if (exception is BadHttpRequestException badRequest)
        {
            await WriteReasonAsync(context, (HttpStatusCode)badRequest.StatusCode, "The request is not valid: " + badRequest.Message);
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

    private static Task WriteReasonAsync(HttpContext context, HttpStatusCode status, string reason)
    {
        context.Response.StatusCode = (int)status;
        return context.Response.WriteAsync(JsonSerializer.Serialize(
            new { statusCode = (int)status, message = reason, reason },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
