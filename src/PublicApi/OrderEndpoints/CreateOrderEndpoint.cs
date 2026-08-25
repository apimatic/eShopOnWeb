using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting
/// payment -- pay it with <c>POST api/orders/{orderId}/pay</c>.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<CreateOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CreateOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders")]
    [SwaggerOperation(
        Summary = "Places an order from catalog items",
        Description = "Places an order from catalog items for the signed-in shopper",
        OperationId = "orders.create",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<CreateOrderResponse>> HandleAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var buyerId = User.Identity!.Name!;
        var shippingAddress = new Address(request.ShipToStreet, request.ShipToCity, request.ShipToState, request.ShipToCountry, request.ShipToZipCode);
        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();

        var order = await _orderPaymentService.PlaceOrderAsync(buyerId, items, shippingAddress, cancellationToken);

        response.OrderId = order.Id;
        response.Order = order.ToDto(null);

        return response;
    }
}
