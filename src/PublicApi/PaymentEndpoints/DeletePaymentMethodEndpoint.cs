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

public class DeletePaymentMethodRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int PaymentMethodId { get; set; }

    public DeletePaymentMethodRequest(string buyerId, int paymentMethodId)
    {
        BuyerId = buyerId;
        PaymentMethodId = paymentMethodId;
    }
}

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among the caller's cards and
/// can no longer be used to pay. One shopper can never delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(user.BuyerId(), paymentMethodId), service, ct);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service, CancellationToken ct)
    {
        var deleted = await service.DeleteCardAsync(request.BuyerId, request.PaymentMethodId, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
