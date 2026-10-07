using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    public int OrderId { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>One response from Adyen, exactly as it arrived.</summary>
public class AdyenResponseDto
{
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>The HTTP status Adyen answered with; null when no response arrived.</summary>
    public int? HttpStatus { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Adyen's JSON response, verbatim — every field, including ones this build does not model.</summary>
    public JsonElement? Response { get; set; }

    /// <summary>Adyen's response body when it was not JSON.</summary>
    public string? RawResponse { get; set; }

    /// <summary>Why no usable response arrived, when that is the case.</summary>
    public string? TransportError { get; set; }
}

public class AdyenPaymentRecordDto
{
    public PaymentAttemptDto Payment { get; set; } = new();
    public List<AdyenResponseDto> AdyenResponses { get; set; } = new();
}

public class AdyenRefundRecordDto
{
    public RefundDto Refund { get; set; } = new();
    public int PaymentAttemptNumber { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public List<AdyenResponseDto> AdyenResponses { get; set; } = new();
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
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public List<AdyenPaymentRecordDto> Payments { get; set; } = new();
    public List<AdyenRefundRecordDto> Refunds { get; set; } = new();
}

/// <summary>
/// Everything Adyen returned for each payment and refund of an order, for support staff. Operators only.
/// </summary>
public class AdyenRecordEndpoint : IEndpoint<IResult, AdyenRecordRequest, IOrderPaymentStore>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/adyen-record",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentStore store, IPaymentGateway paymentGateway) =>
            {
                return await HandleAsync(new AdyenRecordRequest { OrderId = orderId, Currency = paymentGateway.Currency }, store);
            })
            .Produces<AdyenRecordResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(AdyenRecordRequest request, IOrderPaymentStore store)
    {
        var order = await store.GetAsync(request.OrderId, includePaymentRecord: true);
        if (order is null)
            return Results.NotFound();

        var summary = OrderDtoMapper.ToSummary(order, request.Currency);
        var record = order.PaymentRecord.OrderBy(e => e.RecordedAt).ToList();

        return Results.Ok(new AdyenRecordResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            PaymentStatus = summary.PaymentStatus,
            Total = summary.Total,
            Currency = summary.Currency,
            AmountPaid = summary.AmountPaid,
            AmountRefunded = summary.AmountRefunded,
            Payments = order.PaymentAttempts.OrderBy(a => a.AttemptNumber).Select(a => new AdyenPaymentRecordDto
            {
                Payment = OrderDtoMapper.ToDto(a),
                AdyenResponses = record
                    .Where(e => e.RefundId is null && e.PaymentAttemptNumber == a.AttemptNumber)
                    .Select(ToDto).ToList(),
            }).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.CreatedAt).Select(r => new AdyenRefundRecordDto
            {
                Refund = OrderDtoMapper.ToDto(r),
                PaymentAttemptNumber = r.PaymentAttemptNumber,
                RequestedBy = r.RequestedBy,
                AdyenResponses = record.Where(e => e.RefundId == r.Id).Select(ToDto).ToList(),
            }).ToList(),
        });
    }

    private static AdyenResponseDto ToDto(PaymentProviderRecordEntry entry)
    {
        var dto = new AdyenResponseDto
        {
            RecordedAt = entry.RecordedAt,
            HttpStatus = entry.HttpStatus,
            IdempotencyKey = entry.IdempotencyKey,
            TransportError = entry.TransportError,
        };
        if (!string.IsNullOrWhiteSpace(entry.ResponseBody))
        {
            try
            {
                using var json = JsonDocument.Parse(entry.ResponseBody);
                dto.Response = json.RootElement.Clone();
            }
            catch (JsonException)
            {
                dto.RawResponse = entry.ResponseBody;
            }
        }
        return dto;
    }
}
