using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), service, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationQuery request, IReconciliationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ReconciliationQuery request,
        IReconciliationService service,
        CancellationToken cancellationToken)
    {
        var report = await service.ReconcileAsync(request.From, request.To, cancellationToken);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches,
            PayPalOnly = report.PayPalOnly,
            EshopOnly = report.EshopOnly
        });
    }
}

public class ReconciliationQuery : BaseRequest
{
    public ReconciliationQuery(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public IReadOnlyList<ReconciliationMatch> Matches { get; set; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ReconciliationPayPalOnly> PayPalOnly { get; set; } = Array.Empty<ReconciliationPayPalOnly>();
    public IReadOnlyList<ReconciliationEshopOnly> EshopOnly { get; set; } = Array.Empty<ReconciliationEshopOnly>();
}
