using System.Linq;
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
/// Lists the signed-in shopper's saved cards (safe descriptions only). (Flow 2)
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListPaymentMethodsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListPaymentMethodsResponse>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public ListPaymentMethodsEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpGet("api/payment-methods")]
    [SwaggerOperation(
        Summary = "Lists the signed-in shopper's saved cards",
        Description = "Returns safe descriptions of the caller's saved cards - never full card details.",
        OperationId = "paymentMethods.list",
        Tags = new[] { "PaymentEndpoints" })]
    public override async Task<ActionResult<ListPaymentMethodsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var paymentMethods = await _paymentMethodService.ListAsync(buyerId, cancellationToken);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = paymentMethods.Select(pm => pm.ToDto()).ToList()
        };

        return Ok(response);
    }
}
