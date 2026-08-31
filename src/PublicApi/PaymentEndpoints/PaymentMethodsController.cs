using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentWorkflow _workflow;

    public PaymentMethodsController(PaymentWorkflow workflow) => _workflow = workflow;

    [HttpPost]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.SavePaymentMethodAsync(Caller(), request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(
        CancellationToken cancellationToken) =>
        Ok(await _workflow.PaymentMethodsAsync(Caller(), cancellationToken));

    [HttpDelete("{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _workflow.DeletePaymentMethodAsync(Caller(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string Caller() => User.FindFirstValue(ClaimTypes.Name) ??
                               throw new ApiProblemException(StatusCodes.Status401Unauthorized,
                                   "CALLER_IDENTITY_MISSING", "The token does not identify a caller.");
}
