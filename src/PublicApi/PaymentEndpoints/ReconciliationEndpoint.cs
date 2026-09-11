using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions for a date range, lined up against
/// eShop orders so a payment PayPal knows about and eShop doesn't — or the reverse — is
/// visible. The report covers the whole range, not just its first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Reconcile PayPal transactions against eShop orders (operator)", Tags = new[] { "Orders" })]
        async (string from, string to, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(from, to, service, ct))
            .Produces<ReconciliationResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(string from, string to, IOrderPaymentService service, CancellationToken ct)
    {
        if (!TryParse(from, out var fromDate) || !TryParse(to, out var toDate))
            return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });

        try
        {
            var report = await service.ReconcileAsync(fromDate, toDate, ct);
            var response = new ReconciliationResponse(
                report.From, report.To,
                report.PayPalTransactionCount, report.MatchedCount, report.PayPalOnlyCount, report.EShopOnlyCount,
                report.Lines.Select(l => new ReconciliationLineResponse(
                    l.PayPalTransactionId, l.EventCode, l.Status, l.PayPalAmount, l.CurrencyCode,
                    l.FeeAmount, l.InitiationDate, l.OrderId, l.MatchState)).ToList());
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }

    private static bool TryParse(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
}
