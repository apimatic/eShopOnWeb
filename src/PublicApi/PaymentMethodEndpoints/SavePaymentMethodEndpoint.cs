using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// describes it safely (brand, last digits, expiry) — never full card details. Shopper-scoped.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequestDto request, IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                var card = PaymentMapping.ToCardData(request.Card);

                var saved = await paymentService.SaveCardAsync(buyerId, card, ct);

                var response = new SavePaymentMethodResponseDto
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    LastDigits = saved.LastDigits,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName
                };
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<SavePaymentMethodResponseDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
