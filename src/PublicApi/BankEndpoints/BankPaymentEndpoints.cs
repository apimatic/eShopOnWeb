using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public sealed record ChangeShippingRequest(decimal? ShippingCost);

public sealed record ConfirmPaymentRequest(string? Speed);

/// <summary>Pay for an order by bank: start, change the shipping, confirm.</summary>
public class BankPaymentEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (StartBankPaymentRequest request, ClaimsPrincipal user,
                    BankCheckoutService checkout) =>
                {
                    var started = await checkout.StartAsync(Shopper.Id(user), request);
                    return Results.Created($"api/checkout/bank-payment/{started.CheckoutId}", started);
                })
            .WithTags("BankEndpoints");

        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (string checkoutId, ChangeShippingRequest request,
                    ClaimsPrincipal user, BankCheckoutService checkout) =>
                    Results.Ok(await checkout.ChangeShippingAsync(Shopper.Id(user), checkoutId, request.ShippingCost)))
            .WithTags("BankEndpoints");

        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (string checkoutId, ConfirmPaymentRequest request,
                    ClaimsPrincipal user, BankCheckoutService checkout) =>
                    Results.Ok(await checkout.ConfirmAsync(Shopper.Id(user), checkoutId, request.Speed)))
            .WithTags("BankEndpoints");
    }
}
