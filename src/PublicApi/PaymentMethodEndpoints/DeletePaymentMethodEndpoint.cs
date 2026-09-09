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
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes a saved card for the signed-in shopper. Afterwards it no longer appears among the
/// shopper's cards and can no longer be used to pay. Scoped to the caller.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, CancellationToken ct, ISavedCardService service) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId, user.GetUserId(), ct), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service)
    {
        await service.DeleteCardAsync(request.UserId, request.PaymentMethodId, request.Cancellation);
        return Results.NoContent();
    }
}

public record DeletePaymentMethodRequest(int PaymentMethodId, string UserId, CancellationToken Cancellation);
