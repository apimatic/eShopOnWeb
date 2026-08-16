using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Paypal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper by vaulting it with PayPal.
/// The response identifies the saved card and describes it safely (brand/last4) — never full details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = RequestMapper.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var card = RequestMapper.ToCardDetails(request.Card);
                var method = await service.SaveCardAsync(buyerId, new SaveCardInput(card, request.Alias), ct);

                return Results.Created($"api/payment-methods/{method.Id}",
                    new { paymentMethodId = method.Id, paymentMethod = PaymentMapper.ToDto(method) });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
