using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPaymentReconciliationService reconciliation) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, reconciliation);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IPaymentReconciliationService reconciliation)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) ||
            !DateTimeOffset.TryParse(request.To, out var to))
        {
            throw new ApiException("from and to must be ISO-8601 date-times.", 400);
        }

        var report = await reconciliation.GetReportAsync(from, to, request.From!, request.To!, default);
        return Results.Ok(new GetReconciliationResponse
        {
            From = report.From,
            To = report.To,
            LastRefreshedDatetime = report.LastRefreshedDatetime,
            Matched = report.Matched,
            PayPalOnly = report.PayPalOnly,
            EshopOnly = report.EshopOnly
        });
    }
}
