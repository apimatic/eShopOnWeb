using System;
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

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>The identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public SavedCardDto? PaymentMethod { get; set; }
}

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper (vaulted at PayPal).
/// The response describes the card safely; full card details are never stored or returned.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService service) =>
                await HandleAsync(request, service))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.CardNumber))
        {
            return Results.BadRequest(new { message = "Card details are required to save a card." });
        }

        try
        {
            var card = PaymentMapper.ToPaymentCard(request.Card);
            var saved = await service.SaveCardAsync(buyerId, card);
            var response = new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = saved.Id,
                PaymentMethod = PaymentMapper.ToSavedCardDto(saved)
            };
            return Results.Created($"api/payment-methods/{saved.Id}", response);
        }
        catch (PaymentException ex)
        {
            return PaymentResults.FromException(ex);
        }
    }
}
