using System;
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

/// <summary>Returns the signed-in shopper's own orders together with their payment state.</summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MyOrdersResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public MyOrdersEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpGet("api/my-orders")]
    [SwaggerOperation(
        Summary = "Lists the caller's own orders",
        Description = "Returns the signed-in shopper's own orders together with their payment state",
        OperationId = "orders.mine",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<MyOrdersResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new MyOrdersResponse(Guid.NewGuid());
        var buyerId = User.Identity!.Name!;

        var orders = await _orderPaymentService.GetOrdersForBuyerAsync(buyerId, cancellationToken);

        response.Orders = orders.Select(o => o.Order.ToDto(o.Payment)).ToList();

        return response;
    }
}
