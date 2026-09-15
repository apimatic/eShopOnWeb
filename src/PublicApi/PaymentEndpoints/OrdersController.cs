using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly IPaymentService _payments;
    public OrdersController(IPaymentService payments) => _payments = payments;

    [HttpPost]
    public async Task<ActionResult<OrderPaymentView>> Create(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _payments.CreateOrderAsync(BuyerId,
            request.Items.Select(x => new CreateOrderItem(x.CatalogItemId, x.Quantity)).ToList(),
            new ShippingAddress(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country,
                request.ShippingAddress.ZipCode), cancellationToken);
        return Created($"/api/orders/{order.OrderId}", order);
    }

    [HttpPost("{orderId:int}/pay")]
    public async Task<ActionResult<OrderPaymentView>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) => Ok(await _payments.PayAsync(BuyerId, orderId,
        request.Card?.ToModel(), request.PaymentMethodId, cancellationToken));

    [HttpPost("{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderPaymentView>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _payments.FulfilAsync(orderId, cancellationToken));

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderPaymentView>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("{orderId:int}/refunds")]
    public async Task<ActionResult<RefundView>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(BuyerId, orderId, request.Amount,
            request.IdempotencyKey, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{refund.RefundId}", refund);
    }

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}
