using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Helpers;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total: places a hold on the shopper's money without taking it.
/// Pays either with one-off card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, int, PayOrderRequest, HttpContext>
{
    private readonly IOrderPaymentService _paymentService;

    public PayOrderEndpoint(IOrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, request, httpContext);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }
        if (request.Card is null && request.PaymentMethodId is null)
        {
            return Results.BadRequest(new { message = "Provide either 'card' details or a 'paymentMethodId' of a saved card." });
        }

        try
        {
            var payment = await _paymentService.PayAsync(buyerId, orderId, request.Card?.ToCardDetails(), request.PaymentMethodId);

            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                OrderId = orderId,
                PaymentId = payment.Id,
                Status = payment.Status.ToString(),
                Amount = payment.Amount,
                Currency = payment.Currency,
                AuthorizationId = payment.AuthorizationId,
                AuthorizationStatus = payment.AuthorizationStatus,
                AuthorizationExpiresAt = payment.AuthorizationExpiresAt
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EndpointHelpers.MapException(ex);
        }
    }
}

public class PayOrderRequest : BaseRequest
{
    /// <summary>Id of a saved card (POST /api/payment-methods) to pay with.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Never stored.</summary>
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? Cvc { get; set; }
    public string? CardholderName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = $"{ExpiryYear:D4}-{ExpiryMonth:D2}",
        SecurityCode = Cvc,
        CardholderName = CardholderName,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        State = State,
        PostalCode = PostalCode,
        CountryCode = CountryCode
    };
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
}
