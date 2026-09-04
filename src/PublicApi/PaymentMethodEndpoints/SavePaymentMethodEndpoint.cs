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

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved
/// card and describes it safely (brand + last 4 + expiry) - full card details are never
/// stored or returned.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest body, ClaimsPrincipal principal, IPaymentProcessingService paymentProcessing) =>
            {
                return await HandleAsync(new SavePaymentMethodRequest(body, principal.Identity?.Name ?? string.Empty), paymentProcessing);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentProcessingService paymentProcessing)
    {
        var response = new SavePaymentMethodResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card is null)
        {
            throw new ApplicationCore.Exceptions.DomainValidationException("Card details are required to save a card.");
        }

        var paymentMethod = await paymentProcessing.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails(), request.Alias);

        response.PaymentMethodId = paymentMethod.Id;
        response.Brand = paymentMethod.Brand;
        response.Last4 = paymentMethod.Last4;
        response.Expiry = paymentMethod.Expiry;
        response.Alias = paymentMethod.Alias;
        return Results.Created($"/api/payment-methods/{paymentMethod.Id}", response);
    }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public OrderEndpoints.CardRequestDto? Card { get; init; }
    public string? Alias { get; init; }

    /// <summary>Filled from the JWT by the route handler; not bound from the body.</summary>
    public string BuyerId { get; init; } = string.Empty;

    public SavePaymentMethodRequest() { }

    public SavePaymentMethodRequest(SavePaymentMethodRequest source, string buyerId)
    {
        Card = source.Card;
        Alias = source.Alias;
        BuyerId = buyerId;
        _correlationId = source.CorrelationId();
    }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
}
