using System.Collections.Generic;
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
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly IPaymentService _payments;
    public PaymentMethodsController(IPaymentService payments) => _payments = payments;

    [HttpPost]
    public async Task<ActionResult<SavedCardView>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await _payments.SavePaymentMethodAsync(BuyerId, request.Card.ToModel(), cancellationToken);
        return Created($"/api/payment-methods/{saved.PaymentMethodId}", saved);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedCardView>>> Get(CancellationToken cancellationToken) =>
        Ok(await _payments.GetPaymentMethodsAsync(BuyerId, cancellationToken));

    [HttpDelete("{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(BuyerId, paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}
