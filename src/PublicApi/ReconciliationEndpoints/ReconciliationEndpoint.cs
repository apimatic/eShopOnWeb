using System;
using System.Collections.Generic;
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

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; set; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ReconciliationPaypalOnly> PaypalOnly { get; set; } = Array.Empty<ReconciliationPaypalOnly>();
    public IReadOnlyList<ReconciliationEshopOnly> EshopOnly { get; set; } = Array.Empty<ReconciliationEshopOnly>();
}

public class ReconciliationEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderCheckoutService checkout) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromDate) ||
                    !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toDate))
                {
                    throw new CheckoutException(400, "`from` and `to` must be ISO-8601 date-times.");
                }

                var report = await checkout.ReconcileAsync(fromDate, toDate);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Matched = report.Matched,
                    PaypalOnly = report.PaypalOnly,
                    EshopOnly = report.EshopOnly
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService checkout) => Task.FromResult(Results.BadRequest());
}
