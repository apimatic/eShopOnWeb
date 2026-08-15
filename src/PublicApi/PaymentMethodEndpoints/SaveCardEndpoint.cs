using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>POST /api/payment-methods — vault a card for the signed-in shopper (shopper-scoped).</summary>
public class SaveCardEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        async (CardModel request, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                var buyerId = CallerContext.BuyerId(user);
                var card = await service.SaveCardAsync(buyerId, request.ToDetails(), ct);

                return Results.Created($"api/payment-methods/{card.Id}",
                    new SaveCardResponse { PaymentMethodId = card.Id, Card = card });
            })
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
