using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly ICommercePaymentService _payments;

    public PaymentMethodsController(ICommercePaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> Create(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(User.Identity!.Name!, request.Card.ToData(),
            cancellationToken);
        var response = PaymentMethodResponse.From(method);
        return Created($"/api/payment-methods/{method.Id}", response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PaymentMethodResponse>>> List(
        CancellationToken cancellationToken)
    {
        var methods = await _payments.GetPaymentMethodsAsync(User.Identity!.Name!, cancellationToken);
        return Ok(new { paymentMethods = methods.Select(PaymentMethodResponse.From).ToArray() });
    }

    [HttpDelete("{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(paymentMethodId, User.Identity!.Name!, cancellationToken);
        return NoContent();
    }
}
