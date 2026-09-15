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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: PayPal's own transaction records for a date range, lined up against eShop
/// orders so a payment one side knows about and the other doesn't is visible. Covers the whole
/// range, not just its first page. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                IPaymentService paymentService,
                CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                    return Results.BadRequest(new { errors = new[] { "'from' and 'to' must be ISO-8601 date-times." } });

                var result = await paymentService.ReconcileAsync(fromDate, toDate, ct);
                if (!result.IsSuccess) return result.ToProblem();

                var report = result.Value;
                return Results.Ok(new
                {
                    from = report.From,
                    to = report.To,
                    payPalTransactionCount = report.PayPalTransactionCount,
                    matchedCount = report.MatchedCount,
                    payPalOnlyCount = report.PayPalOnlyCount,
                    eShopOnlyCount = report.EShopOnlyCount,
                    entries = report.Entries.Select(e => new
                    {
                        match = e.Match.ToString(),
                        payPalTransactionId = e.PayPalTransactionId,
                        payPalStatus = e.PayPalStatus,
                        payPalAmount = e.PayPalAmount,
                        currency = e.Currency,
                        invoiceId = e.InvoiceId,
                        orderId = e.OrderId,
                        orderCapturedAmount = e.OrderCapturedAmount,
                        orderPaymentStatus = e.OrderPaymentStatus
                    })
                });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ReconciliationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
