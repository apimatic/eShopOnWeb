using System.Collections.Generic;
using System.Linq;
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

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's saved cards.</summary>
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
        Summary = "Lists the caller's saved cards",
        Description = "Returns the signed-in shopper's saved cards (safe descriptions only).",
        OperationId = "paymentMethods.list",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult<ListPaymentMethodsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        var cards = await _paymentMethodService.ListCardsAsync(buyerId, cancellationToken);

        return Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentMethodDto.From).ToList()
        });
    }
}
