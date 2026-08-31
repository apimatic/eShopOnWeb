using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly OrderPaymentService _service;

    public PaymentMethodsController(OrderPaymentService service) => _service = service;

    [HttpPost("api/payment-methods")]
    [ProducesResponseType<SavePaymentMethodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SavePaymentMethodResponse>> Save(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.SavePaymentMethodAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    [ProducesResponseType<IReadOnlyList<PaymentMethodResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await _service.GetPaymentMethodsAsync(BuyerId(), cancellationToken));

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(BuyerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentOperationException(System.Net.HttpStatusCode.Unauthorized,
            "The bearer token does not identify a shopper.");
}
