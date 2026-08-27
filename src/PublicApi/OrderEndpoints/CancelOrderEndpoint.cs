using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the shopper's held funds.
/// </summary>
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithRequest<CancelOrderRequest>
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
        Description = "Voids the payment authorization so the shopper's held funds are released and no money moves. Administrator role required.",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<CancelOrderResponse>> HandleAsync(CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderPaymentService.CancelOrderAsync(request.OrderId, cancellationToken);

        return new CancelOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = order.Payment is null ? null : OrderDtoMapper.ToDto(order.Payment)
        };
    }
}
