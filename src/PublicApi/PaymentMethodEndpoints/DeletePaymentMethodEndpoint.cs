using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears in the shopper's
/// list and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public DeletePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(paymentMethodId, user, ct);
            })
            .Produces<DeletePaymentMethodResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var deleted = await _paymentMethodService.DeleteAsync(buyerId, paymentMethodId, ct);
        if (!deleted)
            return Results.NotFound();

        return Results.Ok(new DeletePaymentMethodResponse
        {
            PaymentMethodId = paymentMethodId,
            Status = "Deleted"
        });
    }
}
