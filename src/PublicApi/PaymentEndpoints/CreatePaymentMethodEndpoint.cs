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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal. The full card number is never
/// stored by this app. Returns the new saved-card id as a top-level field.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public CreatePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            return Results.BadRequest(new { message = "Card details are required to save a card." });
        }

        var response = new CreatePaymentMethodResponse(request.CorrelationId());
        var buyerId = CallerIdentity.GetBuyerId(user);

        var saved = await _paymentMethodService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), request.Alias);

        response.PaymentMethodId = saved.Id;
        response.PaymentMethod = PaymentMethodDto.From(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }

    /// <summary>Optional shopper-chosen nickname for the card.</summary>
    public string? Alias { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>Identifier of the newly saved card.</summary>
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}
