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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders with their payment state. (Flow 1)
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
    [SwaggerOperation(
        Summary = "Lists the signed-in shopper's orders",
        Description = "Returns the caller's orders with their payment state.",
        OperationId = "orders.mine",
        Tags = new[] { "PaymentEndpoints" })]
    public override async Task<ActionResult<MyOrdersResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);

        var response = new MyOrdersResponse
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => o.ToSummary())
                .ToList()
        };

        return Ok(response);
    }
}
