using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions over a date range (all pages)
/// lined up against eShop orders, so discrepancies in either direction are visible.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new GetReconciliationRequest(from, to), reconciliationService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var response = new GetReconciliationResponse(request.CorrelationId());

        if (request.To <= request.From)
        {
            return Results.BadRequest(new { message = "'to' must be after 'from'; both are ISO-8601 date-times." });
        }

        var report = await reconciliationService.GetReconciliationAsync(request.From, request.To);

        response.From = report.From;
        response.To = report.To;
        response.Entries = report.Entries;
        return Results.Ok(response);
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public GetReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public GetReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
}
