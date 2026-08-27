using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards, at PayPal and locally.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<SavedCard> _savedCardRepository;

    public DeletePaymentMethodEndpoint(IPaymentGateway paymentGateway, IRepository<SavedCard> savedCardRepository)
    {
        _paymentGateway = paymentGateway;
        _savedCardRepository = savedCardRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), user, ct);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var savedCard = await _savedCardRepository.GetByIdAsync(request.PaymentMethodId, ct);
        if (savedCard == null || savedCard.BuyerId != buyerId)
        {
            throw new PaymentDomainException($"Saved card {request.PaymentMethodId} was not found.", 404);
        }

        await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, ct);
        await _savedCardRepository.DeleteAsync(savedCard, ct);

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId
        });
    }
}
