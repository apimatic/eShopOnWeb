using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IReadOnlyList<SavedCardDto>>
{
    private readonly IPaymentService _paymentService;

    public ListPaymentMethodsEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("api/payment-methods")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "List the signed-in shopper's saved cards",
        Description = "Returns only the caller's saved cards, without full card details.",
        OperationId = "paymentMethods.list",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult<IReadOnlyList<SavedCardDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var buyerId = PaymentApiMapper.BuyerId(User);
            var cards = await _paymentService.GetPaymentMethodsAsync(buyerId, cancellationToken);
            return Ok(cards);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}
