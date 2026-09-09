using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — saves (vaults) a card for the signed-in shopper. The response identifies
/// the saved card and describes it safely (brand / last four / expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodEndpoint.Args, ISavedCardService>
{
    public record Args(string? BuyerId, SavePaymentMethodRequest Body);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext http, ISavedCardService service) =>
                await HandleAsync(new Args(http.User.GetBuyerId(), request ?? new SavePaymentMethodRequest()), service))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(Args args, ISavedCardService service)
    {
        if (string.IsNullOrEmpty(args.BuyerId))
            return Results.Unauthorized();

        var saved = await service.SaveAsync(args.BuyerId, args.Body.Card.ToCardDetails());
        var dto = SavedCardDto.From(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Card = dto
        });
    }
}

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string?, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ISavedCardService service) =>
                await HandleAsync(http.User.GetBuyerId(), service))
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string? buyerId, ISavedCardService service)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var cards = await service.ListAsync(buyerId);
        return Results.Ok(cards.Select(SavedCardDto.From).ToList());
    }
}

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — removes a saved card; it can no longer be used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodEndpoint.Args, ISavedCardService>
{
    public record Args(int PaymentMethodId, string? BuyerId);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http, ISavedCardService service) =>
                await HandleAsync(new Args(paymentMethodId, http.User.GetBuyerId()), service))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(Args args, ISavedCardService service)
    {
        if (string.IsNullOrEmpty(args.BuyerId))
            return Results.Unauthorized();

        await service.DeleteAsync(args.PaymentMethodId, args.BuyerId);
        return Results.NoContent();
    }
}
