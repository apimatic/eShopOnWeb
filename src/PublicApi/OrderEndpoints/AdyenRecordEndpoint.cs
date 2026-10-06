using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class AdyenRecordRequest : BaseRequest
{
    public AdyenRecordRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class AdyenRecordEntry
{
    /// <summary>"payment" or "refund".</summary>
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? PaymentPspReference { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public string? RefusalReasonCode { get; set; }
    public string? ProviderErrorCode { get; set; }
    public string? ProviderMessage { get; set; }
    public int? HttpStatus { get; set; }
    public string? RequestedBy { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Everything Adyen returned for this call, verbatim (JSON), including fields this build does not model.</summary>
    public JsonElement? AdyenResponse { get; set; }

    /// <summary>The raw body, only when Adyen's answer was not JSON.</summary>
    public string? AdyenResponseText { get; set; }
}

public class AdyenRecordResponse : BaseResponse
{
    public AdyenRecordResponse(Guid correlationId) : base(correlationId)
    {
    }

    public AdyenRecordResponse()
    {
    }

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public List<AdyenRecordEntry> Payments { get; set; } = new();
    public List<AdyenRecordEntry> Refunds { get; set; } = new();
}

/// <summary>
/// Operator/support action: the order's full payment record — every payment attempt and refund with
/// exactly what Adyen returned for it.
/// </summary>
public class AdyenRecordEndpoint : IEndpoint<IResult, AdyenRecordRequest, IOrderPaymentStore>
{
    private readonly AdyenSettings _paymentSettings;

    public AdyenRecordEndpoint(IOptions<AdyenSettings> paymentSettings)
    {
        _paymentSettings = paymentSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/adyen-record",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentStore store) =>
            {
                return await HandleAsync(new AdyenRecordRequest(orderId), store);
            })
            .Produces<AdyenRecordResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(AdyenRecordRequest request, IOrderPaymentStore store)
    {
        var order = await store.GetOrderWithPaymentsAsync(request.OrderId, CancellationToken.None);
        if (order is null)
        {
            return Results.NotFound();
        }

        var summary = OrderSummaryDto.From(order, _paymentSettings.NormalizedCurrency);
        var response = new AdyenRecordResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = summary.Total,
            Currency = summary.Currency,
            PaymentStatus = summary.PaymentStatus,
            AmountPaid = summary.AmountPaid,
            AmountRefunded = summary.AmountRefunded,
            Payments = order.PaymentAttempts.OrderBy(a => a.AttemptNumber).Select(a => WithRaw(new AdyenRecordEntry
            {
                Kind = "payment",
                Id = a.AttemptNumber.ToString(),
                MerchantReference = a.MerchantReference,
                IdempotencyKey = a.IdempotencyKey,
                Status = a.Status.ToString(),
                Amount = MinorUnits.ToDecimal(a.AmountMinorUnits, a.Currency),
                Currency = a.Currency,
                PspReference = a.PspReference,
                ResultCode = a.ResultCode,
                RefusalReason = a.RefusalReason,
                RefusalReasonCode = a.RefusalReasonCode,
                ProviderErrorCode = a.ProviderErrorCode,
                ProviderMessage = a.ProviderMessage,
                HttpStatus = a.ProviderHttpStatus,
                CreatedAt = a.CreatedAt,
                CompletedAt = a.CompletedAt,
            }, a.ProviderResponse)).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.Sequence).Select(r => WithRaw(new AdyenRecordEntry
            {
                Kind = "refund",
                Id = r.RefundId,
                MerchantReference = r.MerchantReference,
                IdempotencyKey = r.IdempotencyKey,
                Status = r.Status.ToString(),
                Amount = MinorUnits.ToDecimal(r.AmountMinorUnits, r.Currency),
                Currency = r.Currency,
                PspReference = r.PspReference,
                PaymentPspReference = r.PaymentPspReference,
                ResultCode = r.ProviderStatus,
                ProviderErrorCode = r.ProviderErrorCode,
                ProviderMessage = r.ProviderMessage,
                HttpStatus = r.ProviderHttpStatus,
                RequestedBy = r.RequestedBy,
                Reason = r.Reason,
                CreatedAt = r.CreatedAt,
                CompletedAt = r.CompletedAt,
            }, r.ProviderResponse)).ToList(),
        };

        return Results.Ok(response);
    }

    private static AdyenRecordEntry WithRaw(AdyenRecordEntry entry, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return entry;
        try
        {
            using var document = JsonDocument.Parse(raw);
            entry.AdyenResponse = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            entry.AdyenResponseText = raw;
        }

        return entry;
    }
}
