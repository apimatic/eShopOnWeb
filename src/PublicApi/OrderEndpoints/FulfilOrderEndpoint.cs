using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized funds.
/// A stale authorization is renewed automatically when possible.
/// </summary>
public class FulfilOrderEndpoint : EndpointBaseAsync
    .WithRequest<FulfilOrderRequest>
    .WithActionResult<FulfilOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public FulfilOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Fulfils an order and captures the payment",
        Description = "Captures the previously authorized funds. The response shows the captured amount, PayPal's fee and the net proceeds. Renews a stale authorization when possible. Administrator role required.",
        OperationId = "orders.fulfil",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<FulfilOrderResponse>> HandleAsync(FulfilOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderPaymentService.FulfilOrderAsync(request.OrderId, cancellationToken);

        return new FulfilOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = order.Payment is null ? null : OrderDtoMapper.ToDto(order.Payment)
        };
    }
}
