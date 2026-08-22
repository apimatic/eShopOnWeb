using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new GetReconciliationRequest(from, to), reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IReconciliationService reconciliationService)
    {
        if (request.From is null || request.To is null)
        {
            throw new CheckoutException("Query parameters 'from' and 'to' are required ISO-8601 date-times.", 400);
        }

        var report = await reconciliationService.ReconcileAsync(request.From.Value, request.To.Value, default);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched,
            PayPalOnly = report.PayPalOnly,
            EshopOnly = report.EshopOnly
        });
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset? From { get; }
    public DateTimeOffset? To { get; }

    public GetReconciliationRequest(DateTimeOffset? from, DateTimeOffset? to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; set; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ProviderTransaction> PayPalOnly { get; set; } = Array.Empty<ProviderTransaction>();
    public IReadOnlyList<ReconciliationOrder> EshopOnly { get; set; } = Array.Empty<ReconciliationOrder>();
}
