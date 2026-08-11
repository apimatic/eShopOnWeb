using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();

    /// <summary>Optional nickname for the card.</summary>
    public string? Alias { get; set; }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = default!;
}

/// <summary>
/// POST /api/payment-methods — save a card for the signed-in shopper (vaulted at PayPal). The response
/// describes the card safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, IPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        var instruction = new SaveCardInstruction(request.Card.ToRawCard(), request.Alias);

        var method = await service.SaveCardAsync(buyerId, instruction);

        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            PaymentMethod = method.ToDto()
        };
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}
