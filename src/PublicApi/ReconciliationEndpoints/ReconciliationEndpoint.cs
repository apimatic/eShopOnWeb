using System;
using System.Globalization;
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

public class ReconciliationEndpoint : IEndpoint<IResult, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService service) =>
            {
                return await HandleAsync(from, to, service);
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IReconciliationService service)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(string from, string to, IReconciliationService service)
    {
        if (!TryParseTimestamp(from, out var fromValue) || !TryParseTimestamp(to, out var toValue))
        {
            throw new PaymentException("'from' and 'to' must be ISO-8601 date-times.", 400);
        }

        var report = await service.ReconcileAsync(fromValue, toValue);
        return Results.Ok(new
        {
            from = report.From,
            to = report.To,
            paypalTransactionCount = report.PaypalTransactionCount,
            eshopOrderCount = report.EshopOrderCount,
            matchedCount = report.MatchedCount,
            paypalOnlyCount = report.PaypalOnlyCount,
            eshopOnlyCount = report.EshopOnlyCount,
            items = report.Items.Select(i => new
            {
                matchStatus = i.MatchStatus,
                orderId = i.OrderId,
                eshopPaymentStatus = i.EshopPaymentStatus,
                paypalTransactionId = i.PayPalTransactionId,
                paypalReferenceId = i.PayPalReferenceId,
                invoiceId = i.InvoiceId,
                amount = i.Amount,
                currency = i.Currency,
                paypalTransactionDate = i.PayPalTransactionDate
            })
        });
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out result);
    }
}
