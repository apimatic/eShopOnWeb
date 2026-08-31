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
/// Authorizes (holds) the order total with PayPal, either with one-off card details
/// or with one of the shopper's saved cards. No money moves until fulfilment.
/// </summary>
public class PayOrderEndpoint : EndpointBaseAsync
    .WithRequest<PayOrderRequest>
    .WithActionResult<PayOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/pay")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Authorizes the order total",
        Description = "Puts a hold on the order total via PayPal; the money is only taken at fulfilment.",
        OperationId = "orders.pay",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<PayOrderResponse>> HandleAsync(PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var orderId = int.Parse((string)RouteData.Values["orderId"]!);
        try
        {
            var payment = await _orderPaymentService.PayOrderAsync(
                buyerId,
                orderId,
                request.Card?.ToGatewayCard(),
                request.PaymentMethodId,
                cancellationToken);

            return new PayOrderResponse(request.CorrelationId())
            {
                OrderId = orderId,
                OrderStatus = "AwaitingFulfilment",
                Payment = PaymentDto.FromPayment(payment)
            };
        }
        catch (OrderNotFoundException)
        {
            return NotFound();
        }
    }
}
