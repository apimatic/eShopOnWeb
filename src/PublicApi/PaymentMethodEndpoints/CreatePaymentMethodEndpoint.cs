using System;
using System.ComponentModel.DataAnnotations;
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
/// Saves a card for the signed-in shopper by vaulting it with PayPal. The response
/// identifies the saved card and describes it safely (brand, last digits, expiry) —
/// full card details are never stored by this application.
/// </summary>
public class CreatePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<CreatePaymentMethodRequest>
    .WithActionResult<CreatePaymentMethodResponse>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public CreatePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    [HttpPost("api/payment-methods")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Saves a card",
        Description = "Vaults the card with PayPal for the authenticated shopper and returns a safe description of it.",
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

        var savedCard = await _paymentMethodService.SaveCardAsync(buyerId, request.Card.ToGatewayCard(), cancellationToken);

        return new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            LastDigits = savedCard.LastDigits,
            Expiry = savedCard.Expiry,
            CardholderName = savedCard.CardholderName
        };
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    [Required]
    public CardDetailsDto Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreatePaymentMethodResponse()
    {
    }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
