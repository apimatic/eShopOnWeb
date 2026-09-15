using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper. The card is vaulted at PayPal;
/// only a safe descriptor is kept. Returns the saved card's id as a top-level field.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), request.Alias, ct);

                var dto = PaymentMethodDto.FromEntity(saved);
                return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", dto);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
