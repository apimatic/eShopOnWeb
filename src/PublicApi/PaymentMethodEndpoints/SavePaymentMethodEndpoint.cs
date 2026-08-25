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
/// Saves a card for the signed-in shopper to reuse on a later order. The raw card number is
/// never stored -- only PayPal's vault token id and a safe descriptor (brand/last 4/expiry).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SavePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<SavePaymentMethodRequest>
    .WithActionResult<SavePaymentMethodResponse>
{
    private readonly ISavedCardService _savedCardService;

    public SavePaymentMethodEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    [HttpPost("api/payment-methods")]
    [SwaggerOperation(
        Summary = "Saves a card for reuse",
        Description = "Saves a card for the signed-in shopper; the raw card number is never stored",
        OperationId = "paymentMethods.create",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult<SavePaymentMethodResponse>> HandleAsync(SavePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var response = new SavePaymentMethodResponse(request.CorrelationId());
        var buyerId = User.Identity!.Name!;

        var paymentMethod = await _savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), cancellationToken);

        response.PaymentMethodId = paymentMethod.Id;
        response.PaymentMethod = PaymentMethodDto.From(paymentMethod);

        return response;
    }
}
