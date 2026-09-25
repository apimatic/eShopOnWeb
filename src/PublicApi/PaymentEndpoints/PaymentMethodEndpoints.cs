using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Saves a card for the signed-in shopper. The response identifies the saved card safely.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardRequestDto request, ISavedCardService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                if (request is null)
                    return Results.BadRequest(new { message = "Card details are required." });

                var view = await service.SaveCardAsync(buyerId, request.ToCardDetails(), ct);
                return Results.Created($"api/payment-methods/{view.PaymentMethodId}", view);
            })
            .Produces<SavedCardView>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>The caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                var cards = await service.ListCardsAsync(buyerId, ct);
                return Results.Ok(cards);
            })
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>Removes a saved card so it can no longer be seen or used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                var removed = await service.DeleteCardAsync(buyerId, paymentMethodId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .WithTags("PaymentEndpoints");
    }
}
