using System.Security.Claims;
using System.Text.Json.Serialization;
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
    public CardRequestDto? Card { get; set; }
    public string? Alias { get; set; }

    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card
/// and describes it safely (brand + last four + expiry) — never full card details. Returns
/// the saved card's id as a top-level field.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user) =>
            {
                request.CallerId = PaymentEndpointHelpers.GetCallerId(user);
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
    {
        if (request.Card is null)
        {
            throw new PaymentException("Card details are required to save a payment method.");
        }

        var pm = await service.SaveCardAsync(request.CallerId, request.Card.ToCardDetails(), request.Alias);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = pm.Id,
            PaymentMethod = PaymentMethodDto.From(pm)
        };
        return Results.Created($"api/payment-methods/{pm.Id}", response);
    }
}
