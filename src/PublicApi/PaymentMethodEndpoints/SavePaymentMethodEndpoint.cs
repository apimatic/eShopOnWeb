using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardInputDto Card { get; set; } = new();
}

public class SavedCardDto
{
    /// <summary>The identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string CardLast4 { get; set; } = string.Empty;
    public string CardExpiry { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// describes it safely; it never contains full card details.
/// </summary>
public class SavePaymentMethodEndpoint
    : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service) =>
                await HandleAsync(request, user, service))
            .Produces<SavedCardDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user,
        IPaymentMethodService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        var c = request.Card;
        var a = c.BillingAddress;
        var card = new CardDetails(c.Number, c.Expiry, c.SecurityCode, c.Name,
            a?.AddressLine1, a?.AddressLine2, a?.AdminArea2, a?.AdminArea1, a?.PostalCode, a?.CountryCode);

        var method = await service.SaveCardAsync(buyerId, card);

        var dto = new SavedCardDto
        {
            PaymentMethodId = method.Id,
            CardBrand = method.CardBrand,
            CardLast4 = method.CardLast4,
            CardExpiry = method.CardExpiry,
            Description = method.Description,
            CreatedAt = method.CreatedAt.ToString("o")
        };

        return Results.Created($"api/payment-methods/{method.Id}", dto);
    }
}
