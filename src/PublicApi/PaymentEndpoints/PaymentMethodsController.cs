using System;
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
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/payment-methods")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly ICommercePaymentService _payments;
    public PaymentMethodsController(ICommercePaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType<PaymentMethodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(BuyerId(), request.Card.ToData(), cancellationToken);
        var response = PaymentResponseMapper.Method(method);
        return Created($"/api/payment-methods/{method.Id}", response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(CancellationToken cancellationToken)
    {
        var methods = await _payments.GetPaymentMethodsAsync(BuyerId(), cancellationToken);
        return Ok(methods.Select(PaymentResponseMapper.Method).ToList());
    }

    [HttpDelete("{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(BuyerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.Identity?.Name ??
        throw new UnauthorizedAccessException("The bearer token does not contain a name claim.");
}
