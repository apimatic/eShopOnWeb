using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Helpers;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it is no longer listed and can
/// no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly ISavedCardService _savedCardService;

    public DeletePaymentMethodEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext httpContext) =>
            {
                return await HandleAsync(paymentMethodId, httpContext);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            await _savedCardService.DeleteAsync(buyerId, paymentMethodId);
            return Results.NoContent();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EndpointHelpers.MapException(ex);
        }
    }
}
