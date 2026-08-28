using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentApplicationService _service;

    public PaymentMethodsController(PaymentApplicationService service) => _service = service;

    [HttpPost]
    public async Task<ActionResult<SavePaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.SavePaymentMethodAsync(Caller(), request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PaymentMethodDto>>> List(
        CancellationToken cancellationToken) =>
        Ok(await _service.GetPaymentMethodsAsync(Caller(), cancellationToken));

    [HttpDelete("{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(Caller(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string Caller() => User.Identity?.Name
        ?? throw new PaymentValidationException("The bearer token does not identify a shopper.");
}
