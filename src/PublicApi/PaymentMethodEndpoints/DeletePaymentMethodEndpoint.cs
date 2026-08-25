using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes a saved card belonging to the signed-in shopper. Afterwards it no longer appears in
/// their saved cards and can no longer be used to pay (removed from PayPal's vault, then from our store).
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal, IRepository<PaymentMethod>>
{
    private readonly IPaymentProvider _paymentProvider;

    public DeletePaymentMethodEndpoint(IPaymentProvider paymentProvider)
    {
        _paymentProvider = paymentProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IRepository<PaymentMethod> paymentMethodRepository) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), user, paymentMethodRepository);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ClaimsPrincipal user, IRepository<PaymentMethod> paymentMethodRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var paymentMethod = await paymentMethodRepository.GetByIdAsync(request.PaymentMethodId);
        if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await _paymentProvider.DeleteSavedCardAsync(paymentMethod.VaultId, CancellationToken.None);
        await paymentMethodRepository.DeleteAsync(paymentMethod);

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId
        });
    }
}
