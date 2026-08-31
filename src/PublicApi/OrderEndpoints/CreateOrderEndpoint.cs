using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts
/// in AwaitingPayment state; pay it through POST api/orders/{orderId}/pay.
/// </summary>
public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<CreateOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly PaymentGatewayOptions _paymentOptions;

    public CreateOrderEndpoint(IOrderPaymentService orderPaymentService, PaymentGatewayOptions paymentOptions)
    {
        _orderPaymentService = orderPaymentService;
        _paymentOptions = paymentOptions;
    }

    [HttpPost("api/orders")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Places an order from catalog items",
        Description = "Creates an order for the authenticated shopper; the order awaits payment.",
        OperationId = "orders.create",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<CreateOrderResponse>> HandleAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var address = new Address(
            request.ShipToAddress.Street,
            request.ShipToAddress.City,
            request.ShipToAddress.State,
            request.ShipToAddress.Country,
            request.ShipToAddress.ZipCode);

        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Units)).ToList();
        var order = await _orderPaymentService.CreateOrderAsync(buyerId, items, address, cancellationToken);

        return new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = _paymentOptions.Currency
        };
    }
}
