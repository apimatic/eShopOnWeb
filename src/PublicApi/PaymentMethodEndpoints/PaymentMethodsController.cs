using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentWorkflowService _payments;

    public PaymentMethodsController(PaymentWorkflowService payments) => _payments = payments;

    [HttpPost("api/payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> Create(CardRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _payments.SavePaymentMethodAsync(CallerId(), request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodResponse>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PaymentMethodResponse>> List(CancellationToken cancellationToken) =>
        _payments.GetPaymentMethodsAsync(CallerId(), cancellationToken);

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(CallerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string CallerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentApiException(StatusCodes.Status401Unauthorized, "The token has no caller identity.");
}
