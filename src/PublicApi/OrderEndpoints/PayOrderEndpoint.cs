using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Full card details for a one-off payment. Never stored.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of a saved card (from POST /api/payment-methods) to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}

public class CardDetailsRequest
{
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM format.</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }

    private static readonly Regex ExpiryFormat = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);

    public GatewayCard ToGatewayCard()
    {
        if (!ExpiryFormat.IsMatch(Expiry))
        {
            throw new System.ArgumentException("Card expiry must be in YYYY-MM format.");
        }
        return new GatewayCard
        {
            Number = Number.Replace(" ", string.Empty),
            Expiry = Expiry,
            SecurityCode = SecurityCode,
            CardholderName = CardholderName,
            BillingAddress = BillingAddress is null ? null : new GatewayCardAddress
            {
                AddressLine1 = BillingAddress.AddressLine1,
                AddressLine2 = BillingAddress.AddressLine2,
                AdminArea2 = BillingAddress.City,
                AdminArea1 = BillingAddress.State,
                PostalCode = BillingAddress.PostalCode,
                CountryCode = BillingAddress.CountryCode ?? string.Empty
            }
        };
    }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Authorizes the order total: puts a hold on the money without taking it.
/// Pay either with full card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : EndpointBaseAsync
    .WithRequest<PayOrderRequest>
    .WithActionResult<PayOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/pay")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Authorizes payment for an order",
        Description = "Puts a hold on the order total using either full card details or a saved card. Idempotent: repeating the call never authorizes twice.",
        OperationId = "orders.pay",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<PayOrderResponse>> HandleAsync(PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }
        if (request.Card is null && request.PaymentMethodId is null)
        {
            return BadRequest("Supply either card details or a paymentMethodId of a saved card.");
        }
        if (request.Card is not null && request.PaymentMethodId is not null)
        {
            return BadRequest("Supply either card details or a paymentMethodId, not both.");
        }

        var orderId = int.Parse(RouteData.Values["orderId"]!.ToString()!);
        var order = await _orderPaymentService.PayOrderAsync(
            buyerId, orderId, request.Card?.ToGatewayCard(), request.PaymentMethodId, cancellationToken);

        return new PayOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = order.Payment is null ? null : OrderDtoMapper.ToDto(order.Payment)
        };
    }
}
