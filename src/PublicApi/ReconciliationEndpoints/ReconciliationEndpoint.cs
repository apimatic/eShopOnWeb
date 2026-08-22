using System;
using System.Collections.Generic;
using System.Linq;
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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService reconciliation) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliation))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentReconciliationService reconciliation)
    {
        if (request.From == default || request.To == default)
        {
            throw new PaymentValidationException("Query parameters 'from' and 'to' are required ISO-8601 date-times.");
        }

        var report = await reconciliation.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.ToList(),
            PayPalOnly = report.PayPalOnly.ToList(),
            EshopOnly = report.EshopOnly.ToList()
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatch> Matches { get; set; } = new();
    public List<ReconciliationPayPalOnly> PayPalOnly { get; set; } = new();
    public List<ReconciliationEshopOnly> EshopOnly { get; set; } = new();
}
