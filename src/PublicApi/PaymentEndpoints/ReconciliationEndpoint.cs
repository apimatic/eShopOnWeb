using System;
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
/// Operator action: reconciles PayPal's own transaction record over a date range against eShop
/// orders, surfacing anything present in one system but not the other. Covers the whole range (all
/// pages). Restricted to the administrator role. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService, CancellationToken ct) =>
            {
                if (to < from)
                    return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });

                var report = await paymentService.ReconcileAsync(from, to, ct);

                var response = new ReconciliationResponseDto
                {
                    From = report.From,
                    To = report.To,
                    PayPalTransactionCount = report.PayPalTransactionCount,
                    EshopCapturedCount = report.Matched.Count + report.InEshopOnly.Count,
                    Matched = report.Matched.Select(m => new ReconciliationMatchDto
                    {
                        OrderId = m.OrderId,
                        CaptureId = m.CaptureId,
                        PayPalAmount = m.PayPalAmount,
                        EshopAmount = m.EshopAmount,
                        PayPalStatus = m.PayPalStatus,
                        EshopStatus = m.EshopStatus
                    }).ToList(),
                    InPayPalOnly = report.InPayPalOnly.Select(t => new ReconciliationTransactionDto
                    {
                        TransactionId = t.TransactionId,
                        Status = t.Status,
                        Amount = t.Amount,
                        Currency = t.Currency,
                        Fee = t.Fee,
                        InitiatedDate = t.InitiatedDate,
                        UpdatedDate = t.UpdatedDate,
                        InvoiceId = t.InvoiceId,
                        CustomField = t.CustomField
                    }).ToList(),
                    InEshopOnly = report.InEshopOnly.Select(e => new ReconciliationEshopEntryDto
                    {
                        OrderId = e.OrderId,
                        CaptureId = e.CaptureId,
                        Amount = e.Amount,
                        Status = e.Status
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponseDto>()
            .WithTags("PaymentEndpoints");
    }
}
