using System;
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

public class SavePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardDetailsDto Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>
/// Saves a card (vaulted with PayPal) for the signed-in shopper to reuse on a later order.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDetailsDto card, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                var request = new SavePaymentMethodRequest { BuyerId = user.Identity!.Name!, Card = card };
                return await HandleAsync(request, savedCardService);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var response = new SavePaymentMethodResponse(request.CorrelationId());

        var paymentMethod = await savedCardService.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails());

        response.PaymentMethodId = paymentMethod.Id;
        response.PaymentMethod = PaymentMethodDto.FromEntity(paymentMethod);
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
