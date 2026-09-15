using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper (Flow 2). The card is vaulted with PayPal; the response
/// identifies the saved card and describes it safely (brand / last four / expiry) — never full
/// card details, which are not stored by this application.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CardRequest request,
                ISavedPaymentMethodService savedPaymentMethodService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                var saved = await savedPaymentMethodService.SaveCardAsync(buyerId, request.ToCardDetails(), cancellationToken);

                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    Card = PaymentViewMapper.ToView(saved)
                };
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

public class SavePaymentMethodResponse
{
    /// <summary>The identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }

    public SavedCardView Card { get; set; } = new();
}
