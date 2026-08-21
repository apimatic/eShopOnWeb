using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();
    public string? Alias { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies and safely describes the
/// saved card — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
                    return Results.BadRequest(new { error = "card details are required." });

                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service)
    {
        var paymentMethod = await service.SaveCardAsync(request.BuyerId!, request.Card.ToDomain(), request.Alias);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethod.Id,
            Brand = paymentMethod.Brand,
            Last4 = paymentMethod.Last4,
            Expiry = paymentMethod.Expiry,
            Alias = paymentMethod.Alias
        };
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
