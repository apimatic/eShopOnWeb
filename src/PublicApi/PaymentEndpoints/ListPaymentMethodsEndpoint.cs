using System.Collections.Generic;
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
/// Lists the signed-in shopper's own saved cards (safe descriptors only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentOrchestrationService service, CancellationToken ct) =>
                await ExecuteAsync(new ListPaymentMethodsRequest(user.Identity!.Name!), service, ct))
            .Produces<IReadOnlyList<SavedCardView>>()
            .WithTags("PaymentMethods");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(ListPaymentMethodsRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var result = await service.GetSavedCardsAsync(request.BuyerId, ct);
        return result.ToHttpResult(Results.Ok);
    }
}
