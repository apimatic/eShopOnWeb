using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines them up against
/// eShop orders, surfacing anything PayPal knows and eShop doesn't (or the reverse). Admin only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext context) => await HandleAsync(new ReconciliationRequest(from, to), context))
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, HttpContext context)
    {
        var service = context.RequestServices.GetRequiredService<IReconciliationService>();
        var report = await service.BuildAsync(request.From, request.To);
        return Results.Ok(report);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}
