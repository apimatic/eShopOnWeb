using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest
{
    [FromRoute(Name = "paymentMethodId")]
    public int PaymentMethodId { get; set; }
}

/// <summary>
/// Removes a saved card for the signed-in shopper. Afterwards it no longer appears among the
/// caller's saved cards and can no longer be used to pay.
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
        Summary = "Deletes a saved card",
        Description = "Removes the card from PayPal's vault and from the shopper's saved cards.",
        OperationId = "paymentMethods.delete",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult> HandleAsync(
        DeletePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        await _paymentMethodService.DeleteCardAsync(buyerId, request.PaymentMethodId, cancellationToken);
        return NoContent();
    }
}
