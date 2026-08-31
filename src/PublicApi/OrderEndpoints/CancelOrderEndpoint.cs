using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the shopper's
/// held funds (the PayPal authorization is voided, so no money ever moves).
/// </summary>
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<CancelOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CancelOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Cancels an order before fulfilment",
        Description = "Operator-only. Voids the PayPal authorization so the held funds are released.",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<CancelOrderResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var orderId = int.Parse((string)RouteData.Values["orderId"]!);
        try
        {
            var order = await _orderPaymentService.CancelOrderAsync(orderId, cancellationToken);
            return new CancelOrderResponse
            {
                OrderId = order.Id,
                OrderStatus = order.Status.ToString()
            };
        }
        catch (OrderNotFoundException)
        {
            return NotFound();
        }
    }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CancelOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
}
