using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;

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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalid => ((int)HttpStatusCode.BadRequest, invalid.Message),
            OrderStateException state => ((int)HttpStatusCode.Conflict, state.Message),
            CatalogItemNotFoundException notFoundItem => ((int)HttpStatusCode.BadRequest, notFoundItem.Message),
            EmptyBasketOnCheckoutException empty => ((int)HttpStatusCode.BadRequest, empty.Message),
            KeyNotFoundException keyNotFound => ((int)HttpStatusCode.NotFound, keyNotFound.Message),
            ArgumentException argument => ((int)HttpStatusCode.BadRequest, argument.Message),
            _ => ((int)HttpStatusCode.InternalServerError, PhoneNumberSanitizer.Sanitize(exception.Message) ?? "An error occurred.")
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = PhoneNumberSanitizer.Sanitize(message) ?? message
        }.ToString());
    }
}
