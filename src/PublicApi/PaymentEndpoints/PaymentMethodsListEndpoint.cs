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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record ListPaymentMethodsRequest(string BuyerId, CancellationToken Ct);

/// <summary>GET /api/payment-methods — the caller's saved cards (safe descriptors only).</summary>
public class PaymentMethodsListEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
                await HandleAsync(new ListPaymentMethodsRequest(user.BuyerId(), ct), service))
            .Produces<IReadOnlyList<SavedCardView>>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService service)
    {
        var cards = await service.ListAsync(request.BuyerId, request.Ct);
        return Results.Ok(cards);
    }
}
