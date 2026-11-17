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

public class DeletePaymentMethodEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult
{
    private readonly IPaymentService _paymentService;

    public DeletePaymentMethodEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Remove one of the signed-in shopper's saved cards",
        Description = "Disables the card at Finix and removes it from the caller's saved cards.",
        OperationId = "paymentMethods.delete",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var buyerId = PaymentApiMapper.BuyerId(User);
            var paymentMethodId = PaymentApiMapper.RouteId(RouteData, "paymentMethodId");
            await _paymentService.DeletePaymentMethodAsync(
                buyerId,
                paymentMethodId,
                cancellationToken);

            return NoContent();
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}
