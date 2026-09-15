using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodResponse
{
    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response describes the card safely — brand,
/// last four, expiry — and never full card details, which are not stored by this app.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreatePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<CardDto>
    .WithActionResult<CreatePaymentMethodResponse>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public CreatePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpPost("api/payment-methods")]
    [SwaggerOperation(
        Summary = "Saves a card for the signed-in shopper",
        Description = "Vaults the card with PayPal and returns a safe description; full card details are never stored.",
        OperationId = "paymentMethods.create",
        Tags = new[] { "PaymentMethodEndpoints" })]
    public override async Task<ActionResult<CreatePaymentMethodResponse>> HandleAsync(
        [FromBody] CardDto request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        var saved = await _paymentMethodService.SaveCardAsync(buyerId, request.ToCardDetails(), cancellationToken);

        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.CardBrand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Created($"api/payment-methods/{saved.Id}", response);
    }
}
