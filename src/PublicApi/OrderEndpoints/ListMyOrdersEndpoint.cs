using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMyOrdersResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public ListMyOrdersEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpGet("api/my-orders")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the caller's orders",
        Description = "Returns the authenticated shopper's orders with their payment state.",
        OperationId = "orders.mine",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<ListMyOrdersResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var orders = await _orderPaymentService.GetOrdersForBuyerAsync(buyerId, cancellationToken);

        return new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Status = o.Order.Status.ToString(),
                Total = o.Order.Total(),
                Items = o.Order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Payment = o.Payment == null ? null : PaymentDto.FromPayment(o.Payment)
            }).ToList()
        };
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
