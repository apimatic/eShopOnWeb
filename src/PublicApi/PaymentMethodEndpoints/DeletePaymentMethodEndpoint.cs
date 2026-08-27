using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards — from this app and from PayPal's vault.
/// Afterwards it no longer appears in the caller's list and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;

    public DeletePaymentMethodEndpoint(
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(paymentMethodId, user, ct);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user) =>
        HandleAsync(paymentMethodId, user, CancellationToken.None);

    private async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var savedCard = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await _paymentGateway.DeleteSavedCardAsync(savedCard.VaultId, ct);
        await _paymentMethodRepository.DeleteAsync(savedCard, ct);

        return Results.NoContent();
    }
}
