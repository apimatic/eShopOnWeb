using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/payment-methods")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentService _payments;
    public PaymentMethodsController(PaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/payment-methods/{method.PaymentMethodId}", method);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PaymentMethodResponse>>> List(
        CancellationToken cancellationToken) => Ok(await _payments.PaymentMethodsAsync(BuyerId(), cancellationToken));

    [HttpDelete("{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(paymentMethodId, BuyerId(), cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentOperationException(401, "The bearer token has no shopper identity.");
}
