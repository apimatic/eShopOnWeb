using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController, Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentEndpointsController : ControllerBase
{
    private readonly CatalogContext _db; private readonly PayPalPaymentService _payments;
    private string Buyer => User.Identity?.Name ?? throw new InvalidOperationException("Authenticated identity is required.");
    public PaymentEndpointsController(CatalogContext db, PayPalPaymentService payments) { _db = db; _payments = payments; }
    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request, CancellationToken ct) { try { var order = await _payments.CreateOrderAsync(Buyer, request, ct); return Ok(new { orderId = order.Id, total = order.Total(), paymentStatus = order.PaymentStatus }); } catch (PaymentApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); } }
    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<IActionResult> Pay(int orderId, PayOrderRequest request, CancellationToken ct) { var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == Buyer, ct); if (order == null) return NotFound(); try { await _payments.PayAsync(order, Buyer, request, ct); return Ok(new { orderId, paymentStatus = order.PaymentStatus, authorizationId = order.PayPalAuthorizationId }); } catch (PaymentApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); } }
    [HttpGet("my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken ct) => Ok(await _db.Orders.Where(x => x.BuyerId == Buyer).OrderByDescending(x => x.OrderDate).Select(x => new { orderId = x.Id, x.OrderDate, total = x.Total(), x.PaymentStatus, x.FulfilmentStatus, x.PayPalAuthorizationId, x.PayPalCaptureId, x.CapturedAmount, x.PayPalFee, x.NetProceeds, x.RefundedAmount }).ToListAsync(ct));
    [HttpPost("payment-methods")]
    public async Task<IActionResult> SaveCard(CreatePaymentMethodRequest request, CancellationToken ct) { try { var method = await _payments.SaveCardAsync(Buyer, request, ct); return Ok(new { paymentMethodId = method.Id, brand = method.Brand, lastFour = method.LastFour, expiryMonth = method.ExpiryMonth, expiryYear = method.ExpiryYear }); } catch (PaymentApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); } }
    [HttpGet("payment-methods")]
    public async Task<IActionResult> Cards(CancellationToken ct) => Ok(await _db.PaymentMethods.Where(x => x.OwnerId == Buyer).Select(x => new { paymentMethodId = x.Id, x.Brand, lastFour = x.LastFour, x.ExpiryMonth, x.ExpiryYear }).ToListAsync(ct));
    [HttpDelete("payment-methods/{id:int}")]
    public async Task<IActionResult> DeleteCard(int id, CancellationToken ct) { var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == Buyer, ct); if (method == null) return NotFound(); try { await _payments.DeleteCardAsync(method, ct); return NoContent(); } catch (PaymentApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); } }
    [HttpPost("orders/{orderId:int}/fulfil"), Authorize(Roles = Constants.Roles.ADMINISTRATORS)]
    public async Task<IActionResult> Fulfil(int orderId, CancellationToken ct) { var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct); if (order == null) return NotFound(); try { await _payments.FulfilAsync(order, ct); return Ok(new { orderId, paymentStatus = order.PaymentStatus, capturedAmount = order.CapturedAmount, paypalFee = order.PayPalFee, netProceeds = order.NetProceeds }); } catch (PaymentApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); } }
    [HttpPost("orders/{orderId:int}/cancel"), Authorize(Roles = Constants.Roles.ADMINISTRATORS)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken ct) { var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct); if (order == null) return NotFound(); try { await _payments.CancelAsync(order, ct); return Ok(new { orderId, paymentStatus = order.PaymentStatus }); } catch (PaymentApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); } }
    [HttpPost("orders/{orderId:int}/refunds")]
    public async Task<IActionResult> Refund(int orderId, RefundRequest request, CancellationToken ct) { var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == Buyer, ct); if (order == null) return NotFound(); try { var refund = await _payments.RefundAsync(order, request.IdempotencyKey, request.Amount, ct); return Ok(new { refundId = refund.Id, refundAmount = refund.Amount, status = refund.Status }); } catch (PaymentApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); } }
    [HttpGet("reconciliation"), Authorize(Roles = Constants.Roles.ADMINISTRATORS)]
    public async Task<IActionResult> Reconciliation(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) { if (to <= from) return BadRequest(new { error = "to must be after from" }); return Ok(await _payments.ReconcileAsync(from, to, ct)); }
}
