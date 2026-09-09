using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class ReconciliationContext
{
    public string? From { get; init; }
    public string? To { get; init; }
    public CancellationToken Ct { get; init; }
}

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, so a payment PayPal knows about that eShop doesn't — or the reverse — is
/// visible. Covers the whole range (all pages).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationContext>
{
    private static readonly Regex OrderRefPattern =
        new(@"eshop-order-(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public ReconciliationEndpoint(IReadRepository<OrderPayment> paymentRepository, IPayPalPaymentGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, CancellationToken ct) =>
                await HandleAsync(new ReconciliationContext { From = from, To = to, Ct = ct }))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationContext context)
    {
        if (!TryParseIso(context.From, out var from) || !TryParseIso(context.To, out var to))
            return Results.BadRequest(new { message = "from and to are required ISO-8601 date-times." });
        if (to < from)
            return Results.BadRequest(new { message = "to must be on or after from." });

        IReadOnlyList<PayPalTransaction> transactions;
        try
        {
            transactions = await _gateway.SearchTransactionsAsync(from, to, context.Ct);
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentResults.FromGatewayException(ex);
        }

        var eShopPayments = await _paymentRepository.ListAsync(context.Ct);
        // Orders where money actually moved (a capture or refund exists) are the ones expected at PayPal.
        var capturedOrderIds = eShopPayments
            .Where(p => p.CaptureId is not null)
            .Select(p => p.OrderId)
            .ToHashSet();

        var matched = new List<ReconciliationRow>();
        var inPayPalNotInEShop = new List<ReconciliationRow>();
        var seenOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            var orderId = ExtractOrderId(tx.CustomField) ?? ExtractOrderId(tx.InvoiceId);
            var row = new ReconciliationRow
            {
                TransactionId = tx.TransactionId,
                Status = tx.Status,
                Amount = tx.Amount,
                Currency = tx.Currency,
                Fee = tx.Fee,
                InvoiceId = tx.InvoiceId,
                CustomField = tx.CustomField,
                InitiationDate = tx.InitiationDate,
                MatchedOrderId = orderId
            };

            if (orderId is { } id && capturedOrderIds.Contains(id))
            {
                seenOrderIds.Add(id);
                matched.Add(row);
            }
            else
            {
                inPayPalNotInEShop.Add(row);
            }
        }

        var inEShopNotInPayPal = capturedOrderIds
            .Where(id => !seenOrderIds.Contains(id))
            .OrderBy(id => id)
            .ToList();

        return Results.Ok(new ReconciliationResponse
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count,
            MatchedCount = matched.Count,
            Matched = matched,
            InPayPalNotInEShop = inPayPalNotInEShop,
            InEShopNotInPayPal = inEShopNotInPayPal
        });
    }

    private static int? ExtractOrderId(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        var match = OrderRefPattern.Match(value);
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationRow> Matched { get; set; } = new();
    public List<ReconciliationRow> InPayPalNotInEShop { get; set; } = new();
    public List<int> InEShopNotInPayPal { get; set; } = new();
}

public class ReconciliationRow
{
    public string? TransactionId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public int? MatchedOrderId { get; set; }
}
