using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// Vaults a card for the signed-in shopper. The response identifies the saved card and describes it
/// safely (brand, last four, expiry) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardDto, ClaimsPrincipal, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDto request, ClaimsPrincipal user, ISavedCardService service) => await HandleAsync(request, user, service))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CardDto request, ClaimsPrincipal user, ISavedCardService service)
    {
        var saved = await service.SaveCardAsync(user.GetBuyerId(), request.ToDomain());
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}
