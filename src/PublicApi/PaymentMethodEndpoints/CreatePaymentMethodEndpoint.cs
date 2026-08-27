using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Vaults a card at PayPal for the signed-in shopper. The response identifies the saved
/// card and shows only safe display attributes — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                return await HandleAsync(request, user, savedCardService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var savedCard = await savedCardService.SaveCardAsync(user.GetBuyerId(), request.Card.ToGatewayCard());

        response.PaymentMethodId = savedCard.Id;
        response.Card = SavedCardDto.FromEntity(savedCard);
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new CardDetailsDto();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public SavedCardDto? Card { get; set; }
}
