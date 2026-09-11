using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record SaveCardContext(SaveCardRequestBody Body, string? BuyerId);
public record MyCardsContext(string? BuyerId);
public record DeleteCardContext(int PaymentMethodId, string? BuyerId);

/// <summary>POST /api/payment-methods — save (vault) a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SaveCardContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequestBody body, IPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(new SaveCardContext(body, PaymentApiHelpers.GetBuyerId(user)), service))
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SaveCardContext ctx, IPaymentMethodService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.BuyerId is null) return Results.Unauthorized();
            if (ctx.Body?.Card is null || string.IsNullOrWhiteSpace(ctx.Body.Card.CardNumber))
                return Results.BadRequest(new { error = "Card details are required." });

            var saved = await service.SaveCardAsync(ctx.BuyerId, ctx.Body.Card.ToCardDetails());
            var response = new SaveCardResponse
            {
                PaymentMethodId = saved.Id,
                Brand = saved.Brand,
                LastDigits = saved.LastDigits,
                Expiry = saved.Expiry,
                CardholderName = saved.CardholderName
            };
            return Results.Created($"api/payment-methods/{saved.Id}", response);
        });
}

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, MyCardsContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(new MyCardsContext(PaymentApiHelpers.GetBuyerId(user)), service))
            .Produces<List<SavedCardView>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(MyCardsContext ctx, IPaymentMethodService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.BuyerId is null) return Results.Unauthorized();
            var cards = await service.GetForBuyerAsync(ctx.BuyerId);
            return Results.Ok(cards.Select(SavedCardView.From).ToList());
        });
}

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — remove a saved card.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeleteCardContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(new DeleteCardContext(paymentMethodId, PaymentApiHelpers.GetBuyerId(user)), service))
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteCardContext ctx, IPaymentMethodService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.BuyerId is null) return Results.Unauthorized();
            await service.DeleteAsync(ctx.BuyerId, ctx.PaymentMethodId);
            return Results.NoContent();
        });
}
