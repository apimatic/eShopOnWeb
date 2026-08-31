using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards, both locally and from
/// PayPal's vault. Afterwards it can no longer be listed or used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult
{
    private readonly IPaymentMethodService _paymentMethodService;

    public DeletePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpDelete("api/payment-methods/{paymentMethodId}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Deletes a saved card",
        Description = "Removes one of the authenticated shopper's saved cards.",
        OperationId = "paymentMethods.delete",
        Tags = new[] { "PaymentMethodEndpoints" })
    ]
    public override async Task<ActionResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var paymentMethodId = int.Parse((string)RouteData.Values["paymentMethodId"]!);
        var deleted = await _paymentMethodService.DeleteSavedCardAsync(buyerId, paymentMethodId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
