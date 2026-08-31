using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentMethodService _paymentMethods;

    public PaymentMethodsController(PaymentMethodService paymentMethods) => _paymentMethods = paymentMethods;

    [HttpPost]
    [ProducesResponseType<PaymentMethodDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodDto>> Create(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _paymentMethods.CreateAsync(BuyerId, request, cancellationToken);
        return Created($"/api/payment-methods/{method.PaymentMethodId}", method);
    }

    [HttpGet]
    public Task<IReadOnlyList<PaymentMethodDto>> List(CancellationToken cancellationToken) =>
        _paymentMethods.ListAsync(BuyerId, cancellationToken);

    [HttpDelete("{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _paymentMethods.DeleteAsync(BuyerId, paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name) ??
        throw new PaymentApiException(401, "UNAUTHENTICATED", "The token does not identify a shopper.");
}
