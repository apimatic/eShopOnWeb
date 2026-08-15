using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>Route-only request carrying the order id.</summary>
public class OrderIdRouteRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds. Renews a stale
/// authorization rather than failing outright; a hold that can no longer be renewed is reported.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class FulfilOrderEndpoint : EndpointBaseAsync
    .WithRequest<OrderIdRouteRequest>
    .WithActionResult<OrderSummaryDto>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly ApplicationCore.Models.PayPal.PayPalSettings _settings;

    public FulfilOrderEndpoint(IOrderPaymentService orderPaymentService,
        ApplicationCore.Models.PayPal.PayPalSettings settings)
    {
        _orderPaymentService = orderPaymentService;
        _settings = settings;
    }

    [HttpPost("api/orders/{orderId}/fulfil")]
    [SwaggerOperation(
        Summary = "Fulfils an order and captures payment (operator)",
        Description = "Captures the held funds; afterwards the payment shows captured amount, PayPal fee and net proceeds.",
        OperationId = "orders.fulfil",
        Tags = new[] { "OrderPaymentEndpoints" })]
    public override async Task<ActionResult<OrderSummaryDto>> HandleAsync(
        OrderIdRouteRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderPaymentService.FulfilOrderAsync(request.OrderId, cancellationToken);
        return Ok(PaymentMappings.ToSummary(order, _settings.Currency));
    }
}
