using System;
using System.Globalization;
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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IReconciliationService reconciliation) =>
                await HandleAsync(new ReconciliationQuery { From = from, To = to }, reconciliation))
            .Produces<ReconciliationReportDto>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IReconciliationService reconciliation)
    {
        if (!DateTimeOffset.TryParse(query.From, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            || !DateTimeOffset.TryParse(query.To, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            throw new PaymentException("Query parameters 'from' and 'to' must be ISO-8601 date-times.");
        }

        var report = await reconciliation.ReconcileAsync(from, to);
        return Results.Ok(report);
    }
}

public class ReconciliationQuery
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}
