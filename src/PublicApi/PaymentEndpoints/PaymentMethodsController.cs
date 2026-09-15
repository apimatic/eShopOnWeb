using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/payment-methods")]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly IPaymentService _payments;

    public PaymentMethodsController(IPaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType(typeof(CreatePaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatePaymentMethodResponse>> Create(
        [FromBody] SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(BuyerId(),
            OrdersController.ToCard(request.Card), cancellationToken);
        return Created($"/api/payment-methods/{method.PaymentMethodId}",
            new CreatePaymentMethodResponse(method.PaymentMethodId, method));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodView>>> List(
        CancellationToken cancellationToken) =>
        Ok(await _payments.GetPaymentMethodsAsync(BuyerId(), cancellationToken));

    [HttpDelete("{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(BuyerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}

public sealed class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
}

public sealed record CreatePaymentMethodResponse(
    int PaymentMethodId,
    PaymentMethodView PaymentMethod);
