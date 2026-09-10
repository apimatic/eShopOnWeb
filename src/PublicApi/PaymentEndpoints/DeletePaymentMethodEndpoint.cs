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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodRequest : BaseRequest { }

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears among
/// their cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                await service.DeleteAsync(paymentMethodId, buyerId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethods");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service) =>
        Task.FromResult(Results.Empty as IResult);
}
