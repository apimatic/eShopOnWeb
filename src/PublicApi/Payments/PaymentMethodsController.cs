using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly PaymentService _payments;

    public PaymentMethodsController(CatalogContext db, PaymentService payments)
    {
        _db = db;
        _payments = payments;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PaymentMethodResponse), (int)HttpStatusCode.Created)]
    public async Task<ActionResult<PaymentMethodResponse>> Create(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(BuyerId(), request.Card, cancellationToken);
        return Created($"/api/payment-methods/{method.Id}", Map(method));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var methods = await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        return Ok(methods.Select(Map).ToList());
    }

    [HttpDelete("{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(paymentMethodId, BuyerId(), cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name) ??
        throw new PaymentWorkflowException(HttpStatusCode.Unauthorized, "The token has no user identity.");

    private static PaymentMethodResponse Map(ApplicationCore.Entities.PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry
    };
}
