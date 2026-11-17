using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentService _payments;

    public PaymentMethodsController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpPost("api/payment-methods")]
    public async Task<ActionResult<SavePaymentMethodResponse>> Create(
        [FromBody] SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Unauthorized();

        var response = await _payments.SavePaymentMethodAsync(buyerId, request, cancellationToken);
        return Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Unauthorized();

        return Ok(await _payments.GetPaymentMethodsAsync(buyerId, cancellationToken));
    }

    [HttpDelete("api/payment-methods/{paymentMethodId}")]
    public async Task<IActionResult> Delete(string paymentMethodId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Unauthorized();

        await _payments.DeletePaymentMethodAsync(buyerId, paymentMethodId, cancellationToken);
        return NoContent();
    }
}
