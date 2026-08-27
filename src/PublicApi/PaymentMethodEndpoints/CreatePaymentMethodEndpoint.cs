using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, savedCardService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var savedCard = await savedCardService.SaveCardAsync(request.BuyerId, request.Card.ToModel());

        response.PaymentMethodId = savedCard.Id;
        response.Card = PaymentMethodDto.FromDomain(savedCard);
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    public CardRequest Card { get; set; } = new CardRequest();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? Card { get; set; }
}
