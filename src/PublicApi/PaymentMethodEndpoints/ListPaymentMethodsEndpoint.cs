using System.Collections.Generic;
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

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
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
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists saved cards",
        Description = "Returns the authenticated shopper's saved cards, described safely.",
        OperationId = "paymentMethods.list",
        Tags = new[] { "PaymentMethodEndpoints" })
    ]
    public override async Task<ActionResult<ListPaymentMethodsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var cards = await _paymentMethodService.GetSavedCardsAsync(buyerId, cancellationToken);

        return new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(c => new PaymentMethodDto
            {
                PaymentMethodId = c.Id,
                Brand = c.Brand,
                LastDigits = c.LastDigits,
                Expiry = c.Expiry,
                CardholderName = c.CardholderName
            }).ToList()
        };
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
