using System;
using System.Security.Claims;
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
    public CardInput Card { get; set; } = new();
    public string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public PaymentMethodView PaymentMethod { get; set; } = new();
}

/// <summary>
/// Saves a card for the signed-in shopper (vaulted in PayPal). The response describes the card
/// safely enough to recognise it and never returns full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
    {
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
            throw new PaymentOperationException("Card details are required to save a card.", 400);

        var method = await service.SaveCardAsync(request.BuyerId, request.Card.ToPayPalCard());

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = method.Id,
            PaymentMethod = PaymentMethodView.From(method)
        };
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}
