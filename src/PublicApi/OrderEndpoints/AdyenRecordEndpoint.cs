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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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

/// <summary>
/// The support record of an order: every payment attempt and refund, each with everything Adyen returned
/// for it, verbatim (including fields this build does not model).
/// </summary>
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
    public string PaymentStatus { get; set; } = string.Empty;
    public string MerchantOrderReference { get; set; } = string.Empty;
    public long PaidAmountMinor { get; set; }
    public long RefundedAmountMinor { get; set; }
    public long RefundableAmountMinor { get; set; }
    public List<AdyenPaymentRecordDto> Payments { get; set; } = new();
    public List<AdyenRefundRecordDto> Refunds { get; set; } = new();
}

public class AdyenPaymentRecordDto
{
    public int PaymentAttemptId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public long? AuthorisedAmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public string? RefusalReasonCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<AdyenResponseDto> AdyenResponses { get; set; } = new();
}

public class AdyenRefundRecordDto
{
    public int RefundId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PaymentPspReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<AdyenResponseDto> AdyenResponses { get; set; } = new();
}

public class AdyenResponseDto
{
    public DateTimeOffset ReceivedAt { get; set; }
    public int HttpStatusCode { get; set; }

    /// <summary>The body exactly as Adyen sent it: a JSON value when it parses as JSON, otherwise the raw text.</summary>
    public object? Body { get; set; }

    public static AdyenResponseDto From(ProviderResponseRecord record)
    {
        object? body;
        try
        {
            using var document = JsonDocument.Parse(record.Body);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            body = record.Body;
        }

        return new AdyenResponseDto { ReceivedAt = record.ReceivedAt, HttpStatusCode = record.HttpStatusCode, Body = body };
    }
}

/// <summary>
/// Operator action: everything Adyen returned for each payment and refund of an order.
/// </summary>
public class AdyenRecordEndpoint : IEndpoint<IResult, AdyenRecordRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/adyen-record",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new AdyenRecordRequest(orderId), paymentService);
            })
            .Produces<AdyenRecordResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(AdyenRecordRequest request, IOrderPaymentService paymentService)
    {
        var order = await paymentService.GetOrderWithPaymentsAsync(request.OrderId, CancellationToken.None);
        if (order is null)
            return Results.NotFound();

        var response = new AdyenRecordResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            PaymentStatus = order.PaymentStatus.ToString(),
            MerchantOrderReference = $"ESHOP-{order.Id}",
            PaidAmountMinor = order.PaidAmountMinor,
            RefundedAmountMinor = order.RefundedAmountMinor,
            RefundableAmountMinor = order.RefundableAmountMinor,
            Payments = order.PaymentAttempts.OrderBy(a => a.CreatedAt).Select(a => new AdyenPaymentRecordDto
            {
                PaymentAttemptId = a.Id,
                Reference = a.Reference,
                IdempotencyKey = a.IdempotencyKey,
                Status = a.Status.ToString(),
                AmountMinor = a.AmountMinor,
                AuthorisedAmountMinor = a.AuthorisedAmountMinor,
                Currency = a.Currency,
                PspReference = a.PspReference,
                ResultCode = a.ResultCode,
                RefusalReason = a.RefusalReason,
                RefusalReasonCode = a.RefusalReasonCode,
                ErrorCode = a.ErrorCode,
                ErrorMessage = a.ErrorMessage,
                CreatedAt = a.CreatedAt,
                CompletedAt = a.CompletedAt,
                AdyenResponses = a.ProviderResponses.OrderBy(r => r.ReceivedAt).Select(AdyenResponseDto.From).ToList()
            }).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.CreatedAt).Select(r => new AdyenRefundRecordDto
            {
                RefundId = r.Id,
                Reference = r.Reference,
                IdempotencyKey = r.IdempotencyKey,
                PaymentPspReference = r.PaymentPspReference,
                Status = r.Status.ToString(),
                AmountMinor = r.AmountMinor,
                Currency = r.Currency,
                RequestedBy = r.RequestedBy,
                PspReference = r.PspReference,
                ErrorCode = r.ErrorCode,
                ErrorMessage = r.ErrorMessage,
                CreatedAt = r.CreatedAt,
                CompletedAt = r.CompletedAt,
                AdyenResponses = r.ProviderResponses.OrderBy(x => x.ReceivedAt).Select(AdyenResponseDto.From).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
