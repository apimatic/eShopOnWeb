using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PaymentMethodsController : ControllerBase
{
    private readonly PaymentMethodService _paymentMethodService;

    public PaymentMethodsController(PaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpPost("api/payment-methods")]
    public async Task<ActionResult<SavedPaymentMethodResponse>> Save(
        [FromBody] SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _paymentMethodService.SaveAsync(
            GetBuyerId(),
            request,
            cancellationToken);

        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    public async Task<ActionResult<SavedPaymentMethodsResponse>> List(CancellationToken cancellationToken)
    {
        return Ok(await _paymentMethodService.ListAsync(GetBuyerId(), cancellationToken));
    }

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _paymentMethodService.DeleteAsync(GetBuyerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string GetBuyerId()
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The bearer token does not contain a user identity.");
        }

        return buyerId;
    }
}

