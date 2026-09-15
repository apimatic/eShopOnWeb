using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentService _payments;

    public PaymentMethodsController(PaymentService payments) => _payments = payments;

    [HttpPost("api/payment-methods")]
    public async Task<ActionResult<PaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken ct)
    {
        var response = await _payments.SaveMethodAsync(OwnerId(), request, ct);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    public Task<IReadOnlyList<PaymentMethodResponse>> List(CancellationToken ct) =>
        _payments.ListMethodsAsync(OwnerId(), ct);

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken ct)
    {
        await _payments.DeleteMethodAsync(OwnerId(), paymentMethodId, ct);
        return NoContent();
    }

    private string OwnerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentOperationException(401, "The token does not contain a shopper identity.");
}
