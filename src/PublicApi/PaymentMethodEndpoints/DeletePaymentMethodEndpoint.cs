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
/// Removes one of the signed-in shopper's own saved cards. Afterwards it no longer appears in
/// the list and can no longer be used to pay (its PayPal vault token is deleted too).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeletePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<DeletePaymentMethodRequest>
    .WithActionResult<DeletePaymentMethodResponse>
{
    private readonly ISavedCardService _savedCardService;

    public DeletePaymentMethodEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    [HttpDelete("api/payment-methods/{paymentMethodId}")]
    [SwaggerOperation(
        Summary = "Removes a saved card",
        Description = "Removes one of the signed-in shopper's own saved cards",
        OperationId = "paymentMethods.delete",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult<DeletePaymentMethodResponse>> HandleAsync(DeletePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId());
        var buyerId = User.Identity!.Name!;

        await _savedCardService.DeleteSavedCardAsync(buyerId, request.PaymentMethodId, cancellationToken);

        response.PaymentMethodId = request.PaymentMethodId;

        return response;
    }
}
