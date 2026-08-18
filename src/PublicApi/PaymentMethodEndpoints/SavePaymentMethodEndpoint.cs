using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save. It is vaulted at PayPal; its number is never stored by this application.</summary>
    public CardDto Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Identifier of the saved card (top-level, so the flow can be driven end to end).</summary>
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
}

/// <summary>POST /api/payment-methods — save (vault) a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint
    : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService, CancellationToken>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SavePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService savedCardService,
                CancellationToken cancellationToken) =>
                await HandleAsync(request, savedCardService, cancellationToken))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService,
        CancellationToken cancellationToken)
    {
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.CardNumber))
            throw new PaymentValidationException("Card details are required to save a card.");

        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var saved = await savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), cancellationToken);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Brand = saved.CardBrand,
            Last4 = saved.Last4,
            Expiry = saved.Expiry
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
