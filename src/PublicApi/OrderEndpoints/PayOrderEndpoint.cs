using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total with PayPal (a hold; no money is taken yet), either with
/// one-off card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService, cancellationToken);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        return await HandleAsync(request, paymentService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        if (request.Card is null && request.PaymentMethodId is null)
        {
            throw new PaymentConflictException("Provide either card details or a saved paymentMethodId.");
        }

        var payment = await paymentService.PayOrderAsync(request.OrderId, request.BuyerId,
            request.Card?.ToPayPalCardDetails(), request.PaymentMethodId, cancellationToken);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Payment = PaymentDto.FromEntity(payment)
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>One-off card details for this payment. Never stored.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of a saved card (POST /api/payment-methods) to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public PayPalCardDetails ToPayPalCardDetails()
    {
        if (!int.TryParse(ExpiryMonth, out var month) || month < 1 || month > 12 ||
            !int.TryParse(ExpiryYear, out var year) || year < 2000)
        {
            throw new PaymentConflictException("Card expiry must be a valid month (1-12) and 4-digit year.");
        }

        return new PayPalCardDetails(
            Number.Replace(" ", string.Empty),
            $"{year:D4}-{month:D2}",
            SecurityCode,
            CardholderName,
            BillingAddress?.AddressLine1,
            BillingAddress?.City,
            BillingAddress?.State,
            BillingAddress?.PostalCode,
            BillingAddress?.CountryCode);
    }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto Payment { get; set; } = new();
}
