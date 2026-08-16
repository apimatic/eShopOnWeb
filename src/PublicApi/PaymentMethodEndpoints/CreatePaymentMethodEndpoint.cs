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

public class CreatePaymentMethodRequest
{
    public CardInfo Card { get; set; } = new();

    /// <summary>Optional nickname to help the shopper recognise the card later.</summary>
    public string? Label { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it in PayPal. The response identifies the
/// saved card and describes it safely (brand, last four, expiry) — never full card details.
/// Returns the payment method id.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreatePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedCardService savedCardService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = request.Card.ToCardDetails();
                var saved = await savedCardService.SaveCardAsync(buyerId, card, request.Label, cancellationToken);

                return Results.Created($"api/payment-methods/{saved.PaymentMethodId}",
                    new { paymentMethodId = saved.PaymentMethodId, paymentMethod = saved });
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }
}
