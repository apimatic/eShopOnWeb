using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

public class ReconciliationRequest : BaseRequest
{
    [JsonIgnore] public DateTimeOffset From { get; set; }
    [JsonIgnore] public DateTimeOffset To { get; set; }
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

/// <summary>
/// Operator action (admin): reconciles PayPal's own transaction record against eShop orders across a
/// date range (covering every page and, for ranges beyond PayPal's 31-day search limit, every window).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IPaymentService service, CancellationToken ct) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }
                return await HandleAsync(
                    new ReconciliationRequest { From = from.Value, To = to.Value, Ct = ct }, service);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PayPalOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, request.Ct);
        return Results.Ok(report);
    }
}
