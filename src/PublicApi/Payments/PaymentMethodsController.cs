using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly CommerceService _service;
    public PaymentMethodsController(CommerceService service) => _service = service;

    [HttpPost]
    [ProducesResponseType<SavePaymentMethodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SavePaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.SavePaymentMethodAsync(UserName(), request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PaymentMethodResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(
        CancellationToken cancellationToken) =>
        Ok(await _service.GetPaymentMethodsAsync(UserName(), cancellationToken));

    [HttpDelete("{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(UserName(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string UserName() => User.Identity?.Name
        ?? throw new UnauthorizedAccessException("The bearer token has no name claim.");
}
