using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Returns the signed-in shopper's own saved cards.</summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListPaymentMethodsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListPaymentMethodsResponse>
{
    private readonly ISavedCardService _savedCardService;

    public ListPaymentMethodsEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    [HttpGet("api/payment-methods")]
    [SwaggerOperation(
        Summary = "Lists the caller's saved cards",
        Description = "Returns the signed-in shopper's own saved cards",
        OperationId = "paymentMethods.mine",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult<ListPaymentMethodsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListPaymentMethodsResponse(Guid.NewGuid());
        var buyerId = User.Identity!.Name!;

        var paymentMethods = await _savedCardService.GetSavedCardsAsync(buyerId, cancellationToken);

        response.PaymentMethods = paymentMethods.Select(PaymentMethodDto.From).ToList();

        return response;
    }
}
