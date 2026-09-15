using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — remove a saved card. Afterwards it no longer appears among
/// the caller's cards and can no longer be used to pay. Scoped to the caller.
/// </summary>
public class DeletePaymentMethodEndpoint
    : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService, CancellationToken>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService savedCardService, CancellationToken cancellationToken) =>
                await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId },
                    savedCardService, cancellationToken))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService savedCardService,
        CancellationToken cancellationToken)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var removed = await savedCardService.DeleteAsync(request.PaymentMethodId, buyerId, cancellationToken);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
