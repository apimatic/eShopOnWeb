using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    [FromRoute(Name = "paymentMethodId")]
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public string Status { get; set; } = "Deleted";
}

/// <summary>
/// Removes one of the authenticated shopper's saved cards, both locally and from PayPal's vault.
/// </summary>
public class DeletePaymentMethodEndpoint : EndpointBaseAsync
    .WithRequest<DeletePaymentMethodRequest>
    .WithActionResult<DeletePaymentMethodResponse>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly ILogger<DeletePaymentMethodEndpoint> _logger;

    public DeletePaymentMethodEndpoint(IPaymentGateway paymentGateway, IRepository<SavedCard> savedCardRepository, ILogger<DeletePaymentMethodEndpoint> logger)
    {
        _paymentGateway = paymentGateway;
        _savedCardRepository = savedCardRepository;
        _logger = logger;
    }

    [HttpDelete("api/payment-methods/{paymentMethodId}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Deletes a saved card",
        Description = "Removes the card from PayPal's vault and from the caller's saved cards. It can no longer be used to pay.",
        OperationId = "paymentMethods.delete",
        Tags = new[] { "PaymentMethodEndpoints" })
    ]
    public override async Task<ActionResult<DeletePaymentMethodResponse>> HandleAsync(DeletePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(request.PaymentMethodId), cancellationToken);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            // Don't leak the existence of another shopper's saved card.
            return NotFound();
        }

        try
        {
            await _paymentGateway.DeleteSavedCardAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode == 404)
        {
            // Already gone from the vault — still remove the local record.
            _logger.LogWarning("Vault token for saved card {PaymentMethodId} was already gone from PayPal.", request.PaymentMethodId);
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        return new DeletePaymentMethodResponse();
    }
}
