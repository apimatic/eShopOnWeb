using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodContext
{
    public int PaymentMethodId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public CancellationToken Ct { get; init; }
}

/// <summary>
/// Removes a saved card. After this it no longer appears among the shopper's saved cards and is deleted
/// from the PayPal vault, so it can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodContext>
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly ILogger<DeletePaymentMethodEndpoint> _logger;

    public DeletePaymentMethodEndpoint(
        IRepository<SavedCard> savedCardRepository,
        IPayPalPaymentGateway gateway,
        ILogger<DeletePaymentMethodEndpoint> logger)
    {
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(new DeletePaymentMethodContext
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = user.GetBuyerId(),
                    Ct = ct
                }))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodContext context)
    {
        var card = await _savedCardRepository.GetByIdAsync(context.PaymentMethodId);
        if (card is null || card.BuyerId != context.BuyerId)
            return Results.NotFound(new { message = "The specified saved payment method was not found." });

        try
        {
            await _gateway.DeleteVaultedCardAsync(card.VaultId, context.Ct);
        }
        catch (PaymentGatewayException ex)
        {
            // Best-effort at PayPal: the limited-release Vault v3 DELETE can transiently return 403. The
            // shopper-facing guarantee — the card no longer appears and can no longer be used to pay — is
            // met by removing the local record regardless; the possibly-orphaned vault token is logged.
            _logger.LogWarning(
                "PayPal vault deletion for saved card {Id} (vault {VaultId}) did not complete ({Message}); " +
                "removing the local record anyway.", card.Id, card.VaultId, ex.Message);
        }

        await _savedCardRepository.DeleteAsync(card);
        return Results.NoContent();
    }
}
