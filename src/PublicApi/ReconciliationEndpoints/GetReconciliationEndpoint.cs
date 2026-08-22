using System;
using System.Collections.Generic;
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

public class ReconciliationQuery
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationItemDto> Items { get; set; } = new();
}

public class ReconciliationItemDto
{
    public string Match { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? EshopPaymentStatus { get; set; }
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? TransactionStatus { get; set; }
    public string? TransactionAmount { get; set; }
    public string? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? TransactionInitiationDate { get; set; }
}

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService reconciliation) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, reconciliation);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IReconciliationService reconciliation)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from) ||
            !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            throw new CheckoutException(400, "from and to must be ISO-8601 date-times.");
        }

        var report = await reconciliation.ReconcileAsync(from, to, default);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Items = report.Items.Select(i => new ReconciliationItemDto
            {
                Match = i.Match,
                OrderId = i.OrderId,
                EshopPaymentStatus = i.EshopPaymentStatus,
                TransactionId = i.PayPal?.TransactionId,
                PaypalReferenceId = i.PayPal?.PaypalReferenceId,
                TransactionStatus = i.PayPal?.TransactionStatus,
                TransactionAmount = i.PayPal?.TransactionAmount,
                FeeAmount = i.PayPal?.FeeAmount,
                InvoiceId = i.PayPal?.InvoiceId,
                CustomField = i.PayPal?.CustomField,
                TransactionInitiationDate = i.PayPal?.TransactionInitiationDate
            }).ToList()
        });
    }
}
