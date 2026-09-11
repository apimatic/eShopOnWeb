using System;
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
    public CardDto? Card { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public SavedCardView PaymentMethod { get; set; } = default!;
}

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper. The response identifies the
/// saved card and describes it safely (never full card details).
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
    {
        if (request.Card is null)
        {
            throw new PaymentOperationException("Card details are required to save a card.");
        }

        var card = request.Card.ToGatewayCard();
        var saved = await service.SaveCardAsync(request.BuyerId, card);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = SavedCardView.From(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
