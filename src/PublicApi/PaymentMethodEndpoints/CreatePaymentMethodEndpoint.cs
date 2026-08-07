using System.Security.Claims;
using System.Text.Json.Serialization;
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

/// <summary>Card to save. The body is the card itself; the owner comes from the token.</summary>
public class SavePaymentMethodRequest : CardDto
{
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse
{
    /// <summary>Identifier of the saved card, returned as a top-level field to drive the flow.</summary>
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>Saves a card for the signed-in shopper. Card data is vaulted with PayPal, never stored here.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                request.BuyerId = user.GetBuyerId() ?? string.Empty;
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints")
            .WithSummary("Save a card for reuse");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var paymentMethod = await service.SaveCardAsync(request.BuyerId, request.ToDomain());
        var dto = PaymentMethodDto.From(paymentMethod);

        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            PaymentMethod = dto
        });
    }
}
