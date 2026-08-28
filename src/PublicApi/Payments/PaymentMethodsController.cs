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
[Route("api/payment-methods")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly PaymentService _payments;
    public PaymentMethodsController(PaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Save(CardRequest card, CancellationToken cancellationToken)
    {
        var method = await _payments.SaveCardAsync(BuyerId(), card, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new
        {
            paymentMethodId = method.Id,
            method.Brand,
            lastFour = method.LastFour,
            method.Expiry
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var methods = await _payments.ListPaymentMethodsAsync(BuyerId(), cancellationToken);
        return Ok(methods.Select(x => new
        {
            paymentMethodId = x.Id,
            x.Brand,
            lastFour = x.LastFour,
            x.Expiry
        }));
    }

    [HttpDelete("{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(paymentMethodId, BuyerId(), cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.Identity?.Name
        ?? throw new PaymentApiException(401, "UNAUTHENTICATED", "A valid bearer token is required.");
}
