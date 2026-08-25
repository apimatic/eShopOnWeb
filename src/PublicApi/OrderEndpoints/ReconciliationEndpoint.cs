using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRow
{
    public string? TransactionId { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public string? InitiatedDate { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? ReferenceId { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, EmptyRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, HttpContext ctx) =>
            {
                return await HandleAsync(new EmptyRequest(), ctx, from, to);
            })
            .Produces<List<ReconciliationRow>>()
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, HttpContext ctx)
        => HandleAsync(request, ctx, string.Empty, string.Empty);

    private async Task<IResult> HandleAsync(EmptyRequest _, HttpContext ctx, string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return Results.BadRequest("Both 'from' and 'to' query parameters are required (ISO-8601).");

        // Normalize to UTC strings for PayPal
        if (!DateTimeOffset.TryParse(from, out var fromDto))
            return Results.BadRequest("Invalid 'from' date-time format. Use ISO-8601.");
        if (!DateTimeOffset.TryParse(to, out var toDto))
            return Results.BadRequest("Invalid 'to' date-time format. Use ISO-8601.");

        var startDate = fromDto.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endDate = toDto.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var sp = ctx.RequestServices;
        var paypalService = sp.GetRequiredService<IPayPalService>();
        var ct = ctx.RequestAborted;

        IReadOnlyList<TransactionRecord> transactions;
        try
        {
            transactions = await paypalService.GetTransactionsAsync(startDate, endDate, ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode ?? 422);
        }

        var rows = new List<ReconciliationRow>();
        foreach (var t in transactions)
        {
            rows.Add(new ReconciliationRow
            {
                TransactionId = t.TransactionId,
                Amount = t.Amount,
                Currency = t.Currency,
                Status = t.Status,
                InitiatedDate = t.InitiatedDate,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                ReferenceId = t.ReferenceId
            });
        }

        return Results.Ok(rows);
    }
}
