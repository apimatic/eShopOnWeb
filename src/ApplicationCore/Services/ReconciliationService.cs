using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Reconciles Finix's own record of authorizations and transfers in a date range against
/// the local order/payment projections. Finix pagination is followed to the end of the range.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IFinixClient _finix;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly FinixSettings _settings;

    public ReconciliationService(IFinixClient finix, IRepository<Payment> paymentRepository,
        IOptions<FinixSettings> settings)
    {
        _finix = finix;
        _paymentRepository = paymentRepository;
        _settings = settings.Value;
    }

    public async Task<ReconciliationReport> GenerateAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "INVALID_DATE_RANGE", "The 'from' date must be before the 'to' date.");
        }

        IReadOnlyList<Models.Finix.FinixAuthorizationResponse> authorizations;
        IReadOnlyList<Models.Finix.FinixTransferResponse> transfers;

        try
        {
            authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);
            transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(502, ex.Code ?? "FINIX_RECONCILIATION_FAILED",
                $"Finix reconciliation could not be completed: {ex.Message}");
        }

        var payments = await _paymentRepository.ListAsync(new AllPaymentsWithRefundsSpec(), cancellationToken);
        var paymentsByAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.AuthorizationId))
            .ToDictionary(p => p.AuthorizationId!, p => p);
        var paymentsByCapture = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.CaptureTransferId))
            .ToDictionary(p => p.CaptureTransferId!, p => p);

        var refundsByProviderId = payments
            .SelectMany(p => p.Refunds
                .Where(r => !string.IsNullOrWhiteSpace(r.ProviderTransferId))
                .Select(r => new { Payment = p, Refund = r }))
            .GroupBy(x => x.Refund.ProviderTransferId!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            Currency = _settings.ResolveCurrency()
        };

        var finixIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var authorization in authorizations)
        {
            finixIds.Add(authorization.Id);
            var matched = paymentsByAuthorization.GetValueOrDefault(authorization.Id);

            report.FinixTransactions.Add(new FinixTransactionView
            {
                Id = authorization.Id,
                Kind = "Authorization",
                AmountCents = authorization.Amount,
                Amount = Money.FromCents(authorization.Amount),
                State = authorization.State ?? "UNKNOWN",
                Currency = authorization.Currency ?? string.Empty,
                CreatedAt = authorization.CreatedAt,
                Matched = matched != null,
                MatchedOrderId = matched?.OrderId,
                MatchedEntity = matched == null ? null : "Authorization"
            });
        }

        foreach (var transfer in transfers)
        {
            finixIds.Add(transfer.Id);

            Payment? matched = null;
            string? matchedEntity = null;

            if (transfer.Type == "REVERSAL" && !string.IsNullOrWhiteSpace(transfer.ParentTransfer))
            {
                matched = paymentsByCapture.GetValueOrDefault(transfer.ParentTransfer);
                if (matched != null)
                {
                    matchedEntity = "Refund";
                }
            }

            if (matched == null)
            {
                matched = paymentsByCapture.GetValueOrDefault(transfer.Id);
                if (matched != null)
                {
                    matchedEntity = "Capture";
                }
            }

            if (matched == null && refundsByProviderId.TryGetValue(transfer.Id, out var refundMatch))
            {
                matched = refundMatch.Payment;
                matchedEntity = "Refund";
            }

            report.FinixTransactions.Add(new FinixTransactionView
            {
                Id = transfer.Id,
                Kind = transfer.Type == "REVERSAL"
                    ? "Refund"
                    : (matchedEntity == "Capture" ? "Capture" : "Transfer"),
                AmountCents = transfer.Amount,
                Amount = Money.FromCents(transfer.Amount),
                State = transfer.State ?? "UNKNOWN",
                Currency = transfer.Currency ?? string.Empty,
                CreatedAt = transfer.CreatedAt,
                ParentId = transfer.ParentTransfer,
                FeeCents = transfer.Fee,
                Matched = matched != null,
                MatchedOrderId = matched?.OrderId,
                MatchedEntity = matchedEntity
            });
        }

        foreach (var payment in payments)
        {
            if (!IsInRange(payment.CreatedAt, from, to) &&
                !finixIds.Contains(payment.AuthorizationId ?? string.Empty) &&
                !finixIds.Contains(payment.CaptureTransferId ?? string.Empty) &&
                !payment.Refunds.Any(r => finixIds.Contains(r.ProviderTransferId ?? string.Empty)))
            {
                continue;
            }

            report.EshopPayments.Add(new EshopPaymentView
            {
                OrderId = payment.OrderId,
                Status = payment.Status.ToString(),
                AmountCents = payment.AmountCents,
                CapturedAmountCents = payment.CapturedAmountCents ?? 0,
                AuthorizationId = payment.AuthorizationId,
                CaptureTransferId = payment.CaptureTransferId,
                RefundTransferIds = payment.Refunds
                    .Where(r => !string.IsNullOrWhiteSpace(r.ProviderTransferId))
                    .Select(r => r.ProviderTransferId!)
                    .ToList()
            });

            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId) &&
                !finixIds.Contains(payment.AuthorizationId))
            {
                report.UnmatchedEshop.Add(new EshopReferenceView
                {
                    OrderId = payment.OrderId,
                    Kind = "Authorization",
                    ProviderId = payment.AuthorizationId,
                    Reason = "eShop has this authorization but Finix returned no matching authorization in the range."
                });
            }

            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId) &&
                !finixIds.Contains(payment.CaptureTransferId))
            {
                report.UnmatchedEshop.Add(new EshopReferenceView
                {
                    OrderId = payment.OrderId,
                    Kind = "Capture",
                    ProviderId = payment.CaptureTransferId,
                    Reason = "eShop has this capture but Finix returned no matching transfer in the range."
                });
            }

            foreach (var refund in payment.Refunds.Where(r => !string.IsNullOrWhiteSpace(r.ProviderTransferId)))
            {
                if (!finixIds.Contains(refund.ProviderTransferId!))
                {
                    report.UnmatchedEshop.Add(new EshopReferenceView
                    {
                        OrderId = payment.OrderId,
                        Kind = "Refund",
                        ProviderId = refund.ProviderTransferId!,
                        Reason = "eShop has this refund but Finix returned no matching transfer in the range."
                    });
                }
            }
        }

        report.UnmatchedFinix = report.FinixTransactions.Where(t => !t.Matched).ToList();

        report.Summary = new ReconciliationSummary
        {
            FinixTransactionCount = report.FinixTransactions.Count,
            EshopReferenceCount = report.EshopPayments.Sum(p =>
                (p.AuthorizationId == null ? 0 : 1) +
                (p.CaptureTransferId == null ? 0 : 1) +
                p.RefundTransferIds.Count),
            MatchedCount = report.FinixTransactions.Count(t => t.Matched),
            UnmatchedFinixCount = report.UnmatchedFinix.Count,
            UnmatchedEshopCount = report.UnmatchedEshop.Count
        };

        return report;
    }

    private static bool IsInRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;
}
