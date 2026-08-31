using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly CommercePaymentService _service;

    public PaymentMethodsController(CommercePaymentService service)
    {
        _service = service;
    }

    [HttpPost("api/payment-methods")]
    [ProducesResponseType<CreatePaymentMethodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatePaymentMethodResponse>> Save(CardRequest request, CancellationToken cancellationToken)
    {
        var method = await _service.SavePaymentMethodAsync(Caller, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new CreatePaymentMethodResponse(method.Id, method.Brand, method.LastFour, method.Expiry));
    }

    [HttpGet("api/payment-methods")]
    [ProducesResponseType<IReadOnlyList<PaymentMethodResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(CancellationToken cancellationToken)
    {
        var methods = await _service.GetPaymentMethodsAsync(Caller, cancellationToken);
        return Ok(methods.Select(m => new PaymentMethodResponse(m.Id, m.Brand, m.LastFour, m.Expiry, m.CreatedAt)).ToList());
    }

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(Caller, paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string Caller => User.Identity?.Name
        ?? throw new UnauthorizedAccessException("The bearer token has no name claim.");
}
