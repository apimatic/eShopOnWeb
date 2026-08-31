using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/payment-methods")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentApplicationService _payments;

    public PaymentMethodsController(PaymentApplicationService payments) => _payments = payments;

    [HttpPost]
    [SwaggerOperation(Summary = "Vaults a card for the caller", Tags = new[] { "PaymentEndpoints" })]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _payments.SavePaymentMethodAsync(CallerId(), request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Lists the caller's saved cards", Tags = new[] { "PaymentEndpoints" })]
    public Task<IReadOnlyList<PaymentMethodResponse>> List(CancellationToken cancellationToken) =>
        _payments.GetPaymentMethodsAsync(CallerId(), cancellationToken);

    [HttpDelete("{paymentMethodId:int}")]
    [SwaggerOperation(Summary = "Deletes one of the caller's saved cards", Tags = new[] { "PaymentEndpoints" })]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(CallerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string CallerId() => User.Identity?.Name ?? string.Empty;
}
