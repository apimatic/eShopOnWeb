using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/payment-methods — save a card for the signed-in shopper. Returns paymentMethodId.</summary>
public class SaveCardEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SaveCardRequest request, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var method = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), ct);
                var dto = PaymentMethodDto.From(method);
                return Results.Created($"api/payment-methods/{method.Id}", new SaveCardResponse
                {
                    PaymentMethodId = method.Id,
                    Card = dto
                });
            })
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }
}

/// <summary>GET /api/payment-methods — the caller's saved cards (shopper).</summary>
public class ListCardsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var cards = await service.GetCardsAsync(buyerId, ct);
                return Results.Ok(cards.Select(PaymentMethodDto.From).ToList());
            })
            .Produces<List<PaymentMethodDto>>()
            .WithTags("PaymentMethods");
    }
}

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — remove a saved card (shopper).</summary>
public class DeleteCardEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                await service.DeleteCardAsync(buyerId, paymentMethodId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethods");
    }
}
