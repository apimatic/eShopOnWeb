using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the authenticated shopper's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MyOrdersResponse>
{
    private readonly IReadRepository<Order> _orderRepository;

    public MyOrdersEndpoint(IReadRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    [HttpGet("api/my-orders")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the caller's orders",
        Description = "Returns the authenticated shopper's orders with their payment state.",
        OperationId = "orders.mine",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<MyOrdersResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), cancellationToken);

        return new MyOrdersResponse
        {
            Orders = orders.Select(OrderDtoMapper.ToDto).ToList()
        };
    }
}
