using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    [Required]
    public int CatalogItemId { get; set; }

    [Range(1, 10000)]
    public int Quantity { get; set; } = 1;
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToAddress() => new(
        Street ?? "N/A", City ?? "N/A", State ?? "N/A", Country ?? "N/A", ZipCode ?? "N/A");
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items. The order starts in AwaitingPayment state.
/// </summary>
public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<CreateOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IOrderPaymentService orderPaymentService, PayPalSettings payPalSettings)
    {
        _orderPaymentService = orderPaymentService;
        _payPalSettings = payPalSettings;
    }

    [HttpPost("api/orders")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Places an order from catalog items",
        Description = "Creates an order for the authenticated shopper at current catalog prices. The order starts in AwaitingPayment state.",
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
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest("An order must contain at least one item.");
        }

        var order = await _orderPaymentService.CreateOrderAsync(
            buyerId,
            request.Items.Select(i => new OrderItemLine(i.CatalogItemId, i.Quantity)).ToList(),
            request.ShipToAddress?.ToAddress() ?? new ShipToAddressRequest().ToAddress(),
            cancellationToken);

        var dto = OrderDtoMapper.ToDto(order);
        return Created($"api/my-orders", new CreateOrderResponse
        {
            OrderId = dto.OrderId,
            Status = dto.Status,
            Total = dto.Total,
            Currency = _payPalSettings.Currency,
            Items = dto.Items
        });
    }
}
