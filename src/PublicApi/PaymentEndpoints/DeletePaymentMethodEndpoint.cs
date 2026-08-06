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
/// Removes one of the signed-in shopper's saved cards (from PayPal's vault and locally).
/// A shopper can only delete their own card. (Flow 2)
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeletePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<DeletePaymentMethodRequest>
    .WithActionResult
{
    private readonly IPaymentMethodService _paymentMethodService;

    public DeletePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpDelete("api/payment-methods/{paymentMethodId}")]
    [SwaggerOperation(
        Summary = "Removes a saved card",
        Description = "Deletes one of the caller's saved cards so it can no longer be used to pay.",
        OperationId = "paymentMethods.delete",
        Tags = new[] { "PaymentEndpoints" })]
    public override async Task<ActionResult> HandleAsync(
        DeletePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var removed = await _paymentMethodService.DeleteAsync(buyerId, request.PaymentMethodId, cancellationToken);
        if (!removed)
        {
            return NotFound();
        }

        return NoContent();
    }
}
