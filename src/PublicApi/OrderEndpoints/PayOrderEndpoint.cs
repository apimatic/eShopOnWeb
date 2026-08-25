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
/// Authorizes (holds) the order total -- does not capture it yet. Pays with either raw card
/// details or one of the shopper's saved cards. Idempotent in effect: calling this again after
/// the order is already authorized returns the existing authorization instead of holding funds
/// a second time.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
    [SwaggerOperation(
        Summary = "Authorizes payment for an order",
        Description = "Puts a hold on the order total via a card or a saved card; does not capture it",
        OperationId = "orders.pay",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<PayOrderResponse>> HandleAsync(PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var response = new PayOrderResponse(request.CorrelationId());
        var buyerId = User.Identity!.Name!;

        var payment = await _orderPaymentService.AuthorizePaymentAsync(
            buyerId,
            request.OrderId,
            request.Body.Card?.ToCardDetails(),
            request.Body.SavedPaymentMethodId,
            cancellationToken);

        response.OrderId = request.OrderId;
        response.Payment = payment.ToDto();

        return response;
    }
}
