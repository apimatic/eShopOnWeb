using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — remove a saved card. Afterwards it no longer
/// appears among the caller's cards and is no longer usable to pay (its vault token is deleted).
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, service, user))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        await service.DeleteCardAsync(buyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
