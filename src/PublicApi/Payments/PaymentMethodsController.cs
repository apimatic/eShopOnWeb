using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentService _payments;

    public PaymentMethodsController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SavePaymentMethodResponse), (int)HttpStatusCode.Created)]
    public async Task<ActionResult<SavePaymentMethodResponse>> Save(
        [FromBody] SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(Caller(), request, cancellationToken);
        var response = new SavePaymentMethodResponse(method.PaymentMethodId, method);
        return Created($"/api/payment-methods/{method.PaymentMethodId}", response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaymentMethodsResponse), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<PaymentMethodsResponse>> List(CancellationToken cancellationToken)
    {
        return Ok(new PaymentMethodsResponse(await _payments.GetPaymentMethodsAsync(Caller(), cancellationToken)));
    }

    [HttpDelete("{paymentMethodId:int}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(Caller(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string Caller() => User.Identity?.Name ?? string.Empty;
}
