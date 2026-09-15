using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// POST /api/payment-methods — save a card for the signed-in shopper (vaulted at PayPal). The
/// response identifies the saved card and describes it safely; never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedCardService savedCardService,
                CancellationToken ct) =>
            {
                if (request.Card == null)
                {
                    throw new PaymentConflictException("A card is required to save a payment method.");
                }

                var buyerId = CallerIdentity.BuyerId(user);
                var saved = await savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), ct);

                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    LastDigits = saved.LastDigits,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName
                };
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
