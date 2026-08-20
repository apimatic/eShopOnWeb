using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

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

        if (exception is DuplicateException duplicationException)
        {
            await WriteAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is UnusableContactNumberException unusable)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, unusable.Message);
        }
        else if (exception is OrderStateException orderState)
        {
            await WriteAsync(context, HttpStatusCode.Conflict, orderState.Message);
        }
        else if (exception is KeyNotFoundException notFound)
        {
            await WriteAsync(context, HttpStatusCode.NotFound, notFound.Message);
        }
        else if (exception is ArgumentException argument)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, argument.Message);
        }
        else if (exception is EmptyBasketOnCheckoutException empty)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, empty.Message);
        }
        else if (exception is TwilioRequestException)
        {
            await WriteAsync(context, HttpStatusCode.BadGateway, "The messaging provider request failed.");
        }
        else
        {
            await WriteAsync(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
