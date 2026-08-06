using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper (vaulted with PayPal). Returns a safe description
/// and the new paymentMethodId - never full card details. (Flow 2)
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SavePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<SavePaymentMethodRequest>
    .WithActionResult<SavePaymentMethodResponse>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public SavePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpPost("api/payment-methods")]
    [SwaggerOperation(
        Summary = "Saves a card for the signed-in shopper",
        Description = "Vaults the card with PayPal and returns a safe description plus the new paymentMethodId.",
        OperationId = "paymentMethods.save",
        Tags = new[] { "PaymentEndpoints" })]
    public override async Task<ActionResult<SavePaymentMethodResponse>> HandleAsync(
        [FromBody] SavePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.Expiry))
        {
            return BadRequest("Card number and expiry are required.");
        }

        var paymentMethod = await _paymentMethodService.SaveCardAsync(buyerId, request.ToCardDetails(), cancellationToken);

        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = paymentMethod.Id,
            PaymentMethod = paymentMethod.ToDto()
        };

        return Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
