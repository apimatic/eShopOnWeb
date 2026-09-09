using System;
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
/// Operator report: lists PayPal's own transaction records for a date range and lines them up
/// against eShop orders, so a payment known to one side but not the other is visible. Covers the
/// whole range (all pages), not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, (DateTimeOffset From, DateTimeOffset To), IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
                await HandleAsync((from, to), paymentService))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync((DateTimeOffset From, DateTimeOffset To) request, IPaymentService paymentService)
    {
        var report = await paymentService.ReconcileAsync(request.From, request.To);
        return Results.Ok(ReconciliationResponse.Create(Guid.NewGuid(), report));
    }
}
