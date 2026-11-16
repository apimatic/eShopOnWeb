using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/payment-methods")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PaymentMethodsController : ControllerBase
{
    private readonly PaymentService _payments;

    public PaymentMethodsController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpPost]
    public async Task<ActionResult<SavedCardSummary>> SaveCard([FromBody] CardRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.SaveCardAsync(BuyerId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedCardSummary>>> GetCards(CancellationToken cancellationToken)
    {
        var result = await _payments.GetCardsAsync(BuyerId(), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{paymentMethodId:int}")]
    public async Task<IActionResult> DeleteCard(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeleteCardAsync(BuyerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId()
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The request is not authenticated.");
        }

        return buyerId;
    }
}
