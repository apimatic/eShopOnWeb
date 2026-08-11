using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    /// <summary>Card to vault. Full details flow to PayPal and are never stored by this app.</summary>
    public CardInputDto Card { get; set; } = new();

    /// <summary>Optional shopper-friendly label.</summary>
    public string? Label { get; set; }
}

/// <summary>A saved card described safely enough to recognise it — never full card details.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public string? Label { get; set; }
}

public class SavePaymentMethodResponse : SavedCardDto
{
}

/// <summary>
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. Returns the saved
/// card id and a safe descriptor.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service,
             CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var c = request.Card;
                var card = new CardDetails(c.Number, c.ExpiryMonth, c.ExpiryYear, c.SecurityCode,
                    c.CardholderName, c.BillingLine1, c.BillingCity, c.BillingState,
                    c.BillingPostalCode, c.BillingCountryCode);

                var saved = await service.SaveCardAsync(buyerId, card, request.Label, ct);

                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    Last4 = saved.Last4,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName,
                    Label = saved.Label
                };
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
