using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. The card is vaulted at PayPal; only
/// safe display attributes (brand, last four digits, expiry) are kept locally.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext httpContext, ISavedPaymentMethodService paymentMethodService) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name;
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        if (request.Card is null)
        {
            throw new ArgumentException("Card details are required.");
        }

        var saved = await paymentMethodService.SaveCardAsync(request.BuyerId!, request.Card.ToPayPalCardDetails());

        response.PaymentMethod = PaymentMethodDto.From(saved);
        response.PaymentMethodId = saved.Id;

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public CardRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new PaymentMethodDto();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod saved)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            ExpiryMonth = saved.ExpiryMonth,
            ExpiryYear = saved.ExpiryYear,
            CardholderName = saved.CardholderName,
            CreatedAt = saved.CreatedAt
        };
    }
}
