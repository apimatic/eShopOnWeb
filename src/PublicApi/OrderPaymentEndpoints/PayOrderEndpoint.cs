using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class PayOrderPayload
{
    /// <summary>Raw card details for a one-off payment. Provide this or <see cref="PaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }

    [FromBody]
    public PayOrderPayload Payment { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) the order total with PayPal. Does not take the money. Idempotent in effect:
/// a double-click never authorizes the shopper twice.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PayOrderEndpoint : EndpointBaseAsync
    .WithRequest<PayOrderRequest>
    .WithActionResult<OrderSummaryDto>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly ApplicationCore.Models.PayPal.PayPalSettings _settings;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService,
        ApplicationCore.Models.PayPal.PayPalSettings settings)
    {
        _orderPaymentService = orderPaymentService;
        _settings = settings;
    }

    [HttpPost("api/orders/{orderId}/pay")]
    [SwaggerOperation(
        Summary = "Authorizes payment for an order",
        Description = "Holds the order total with PayPal using card details or a saved card; does not capture.",
        OperationId = "orders.pay",
        Tags = new[] { "OrderPaymentEndpoints" })]
    public override async Task<ActionResult<OrderSummaryDto>> HandleAsync(
        PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        var command = new PayOrderCommand
        {
            Card = request.Payment?.Card?.ToCardDetails(),
            PaymentMethodId = request.Payment?.PaymentMethodId
        };

        var order = await _orderPaymentService.PayOrderAsync(buyerId, request.OrderId, command, cancellationToken);
        return Ok(PaymentMappings.ToSummary(order, _settings.Currency));
    }
}
