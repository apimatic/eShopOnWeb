using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// Lists the authenticated shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListPaymentMethodsResponse>
{
    private readonly IReadRepository<SavedCard> _savedCardRepository;

    public ListPaymentMethodsEndpoint(IReadRepository<SavedCard> savedCardRepository)
    {
        _savedCardRepository = savedCardRepository;
    }

    [HttpGet("api/payment-methods")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the caller's saved cards",
        Description = "Returns safe display details only — never full card details.",
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

        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);

        return new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(c => new PaymentMethodDto
            {
                PaymentMethodId = c.Id,
                CardBrand = c.CardBrand,
                LastDigits = c.LastDigits,
                Expiry = c.Expiry,
                CardholderName = c.CardholderName
            }).ToList()
        };
    }
}
