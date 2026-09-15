using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>The signed-in shopper's own orders with their payment state.</summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MyOrdersResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly ApplicationCore.Models.PayPal.PayPalSettings _settings;

    public MyOrdersEndpoint(IOrderPaymentService orderPaymentService,
        ApplicationCore.Models.PayPal.PayPalSettings settings)
    {
        _orderPaymentService = orderPaymentService;
        _settings = settings;
    }

    [HttpGet("api/my-orders")]
    [SwaggerOperation(
        Summary = "Lists the caller's orders with payment state",
        Description = "Returns the signed-in shopper's own orders and their pay/fulfil/refund state.",
        OperationId = "orders.mine",
        Tags = new[] { "OrderPaymentEndpoints" })]
    public override async Task<ActionResult<MyOrdersResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        var orders = await _orderPaymentService.GetOrdersForBuyerAsync(buyerId, cancellationToken);

        return Ok(new MyOrdersResponse
        {
            Orders = orders.Select(o => PaymentMappings.ToSummary(o, _settings.Currency)).ToList()
        });
    }
}
