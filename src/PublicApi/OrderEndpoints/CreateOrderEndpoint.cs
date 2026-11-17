using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<OrderDto>
{
    private readonly IPaymentService _paymentService;

    public CreateOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/orders")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Create an order awaiting payment",
        Description = "Creates an order from catalog items for the signed-in shopper. The order starts awaiting payment.",
        OperationId = "orders.create",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderDto>> HandleAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var buyerId = PaymentApiMapper.BuyerId(User);
            var requestedItems = request.Items != null && request.Items.Count > 0
                ? request.Items
                : request.OrderItems ?? new List<CreateOrderItemRequest>();
            var items = requestedItems
                .Select(i => new OrderItemInput(i.CatalogItemId, i.Quantity))
                .ToList();

            var shippingAddress = request.ShippingAddress == null
                ? null
                : new ShippingAddressInput(
                    request.ShippingAddress.Street,
                    request.ShippingAddress.City,
                    request.ShippingAddress.State,
                    request.ShippingAddress.Country,
                    request.ShippingAddress.ZipCode);

            var order = await _paymentService.CreateOrderAsync(
                buyerId,
                items,
                shippingAddress,
                cancellationToken);

            return Created($"api/orders/{order.OrderId}", order);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public List<CreateOrderItemRequest>? OrderItems { get; set; }
    public CreateOrderShippingAddressRequest? ShippingAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
