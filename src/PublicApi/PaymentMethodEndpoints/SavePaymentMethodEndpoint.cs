using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal. Full card details
/// are never stored locally; the response carries only safe display data.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService, cancellationToken);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
    {
        return await HandleAsync(request, paymentService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        var saved = await paymentService.SaveCardAsync(request.BuyerId, request.Card.ToPayPalCardDetails(), cancellationToken);

        return Results.Ok(new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Card = SavedCardDto.FromEntity(saved)
        });
    }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardDetailsRequest Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public SavedCardDto Card { get; set; } = new();
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? CardholderName { get; set; }

    public static SavedCardDto FromEntity(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Brand = saved.Brand,
        LastFourDigits = saved.LastFourDigits,
        ExpiryMonth = saved.ExpiryMonth,
        ExpiryYear = saved.ExpiryYear,
        CardholderName = saved.CardholderName
    };
}
