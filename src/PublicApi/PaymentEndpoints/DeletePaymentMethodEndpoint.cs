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

/// <summary>
/// Removes one of the signed-in shopper's own saved cards. Afterwards it no longer appears among the caller's
/// saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentOrchestrationService service, CancellationToken ct) =>
                await ExecuteAsync(new DeletePaymentMethodRequest(paymentMethodId, user.Identity!.Name!), service, ct))
            .WithTags("PaymentMethods");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(DeletePaymentMethodRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var result = await service.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId, ct);
        return result.ToHttpResult(_ => Results.NoContent());
    }
}
