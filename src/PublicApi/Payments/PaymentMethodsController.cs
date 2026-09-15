using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/payment-methods")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentWorkflowService _workflow;

    public PaymentMethodsController(PaymentWorkflowService workflow) => _workflow = workflow;

    [HttpPost]
    [ProducesResponseType(typeof(PaymentMethodResponse), 201)]
    public async Task<ActionResult<PaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.SavePaymentMethodAsync(BuyerId, request.Card, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodResponse>), 200)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(
        CancellationToken cancellationToken) =>
        Ok(await _workflow.GetPaymentMethodsAsync(BuyerId, cancellationToken));

    [HttpDelete("{paymentMethodId:int}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _workflow.DeletePaymentMethodAsync(BuyerId, paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name) ??
        throw new System.UnauthorizedAccessException("The bearer token has no name claim.");
}
