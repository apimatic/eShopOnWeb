using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total via PayPal. Accepts either one-off card
/// details or the id of one of the caller's saved cards. Money is not taken
/// until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, httpContext);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId();

        PayPalCardDetails? card = null;
        if (request.Card != null)
        {
            card = new PayPalCardDetails
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                CardholderName = request.Card.CardholderName,
                BillingAddress = request.Card.BillingAddress == null ? null : new PayPalAddress
                {
                    AddressLine1 = request.Card.BillingAddress.AddressLine1,
                    AddressLine2 = request.Card.BillingAddress.AddressLine2,
                    AdminArea2 = request.Card.BillingAddress.AdminArea2,
                    AdminArea1 = request.Card.BillingAddress.AdminArea1,
                    PostalCode = request.Card.BillingAddress.PostalCode,
                    CountryCode = request.Card.BillingAddress.CountryCode
                }
            };
        }

        var payment = await _paymentService.AuthorizePaymentAsync(buyerId, request.OrderId,
            card, request.PaymentMethodId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Payment = PaymentDto.FromPayment(payment)
        };
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Id of a saved card (from POST api/payment-methods).</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Never stored.</summary>
    public CardDetailsDto? Card { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) {}
    public PayOrderResponse() {}

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
