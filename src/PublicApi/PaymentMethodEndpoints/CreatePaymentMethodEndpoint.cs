using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    [Required]
    public CardDetailsRequest Card { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper so later orders can be paid without re-entering it.
/// Full card details go straight to PayPal's vault and are never stored or logged here.
/// </summary>
public class CreatePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<CreatePaymentMethodRequest>
    .WithActionResult<CreatePaymentMethodResponse>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<SavedCard> _savedCardRepository;

    public CreatePaymentMethodEndpoint(IPaymentGateway paymentGateway, IRepository<SavedCard> savedCardRepository)
    {
        _paymentGateway = paymentGateway;
        _savedCardRepository = savedCardRepository;
    }

    [HttpPost("api/payment-methods")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Saves a card for the shopper",
        Description = "Vaults the card with PayPal and keeps only safe display details (brand, last digits, expiry).",
        OperationId = "paymentMethods.create",
        Tags = new[] { "PaymentMethodEndpoints" })
    ]
    public override async Task<ActionResult<CreatePaymentMethodResponse>> HandleAsync(CreatePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var vaulted = await _paymentGateway.SaveCardAsync(request.Card.ToGatewayCard(), VaultCustomerId.For(buyerId), cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        return Created("api/payment-methods", new CreatePaymentMethodResponse
        {
            PaymentMethodId = savedCard.Id,
            CardBrand = savedCard.CardBrand,
            LastDigits = savedCard.LastDigits,
            Expiry = savedCard.Expiry,
            CardholderName = savedCard.CardholderName
        });
    }
}
