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

/// <summary>
/// Operator action: the support record of an order — every payment attempt and refund with everything Adyen returned.
/// </summary>
public class GetAdyenRecordEndpoint : IEndpoint<IResult, GetAdyenRecordRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/adyen-record",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new GetAdyenRecordRequest(orderId), orderPaymentService);
            })
            .Produces<AdyenRecordResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetAdyenRecordRequest request, IOrderPaymentService orderPaymentService)
    {
        var order = await orderPaymentService.GetOrderAsync(request.OrderId, CancellationToken.None);
        if (order is null)
            return Results.NotFound();

        var responses = order.ProviderResponses.OrderBy(r => r.ReceivedAt).ThenBy(r => r.Id).ToList();
        var response = new AdyenRecordResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Payments = order.PaymentAttempts.OrderBy(a => a.AttemptNumber).Select(a => new AdyenPaymentRecordDto
            {
                AttemptNumber = a.AttemptNumber,
                Reference = a.Reference,
                Status = a.Status.ToString(),
                Amount = a.Amount,
                AmountInMinorUnits = a.AmountInMinorUnits,
                Currency = a.Currency,
                PspReference = a.PspReference,
                ResultCode = a.ResultCode,
                RefusalReason = a.RefusalReason,
                RefusalReasonCode = a.RefusalReasonCode,
                ErrorCode = a.ErrorCode,
                ErrorMessage = a.ErrorMessage,
                SettlesAttemptNumber = a.SettlesAttemptNumber,
                CreatedAt = a.CreatedAt,
                CompletedAt = a.CompletedAt,
                AdyenResponses = ToDtos(responses.Where(r =>
                    r.Operation == PaymentProviderResponse.PaymentOperation && r.PaymentAttemptNumber == a.AttemptNumber))
            }).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.Sequence).Select(r => new AdyenRefundRecordDto
            {
                RefundId = r.RefundId,
                Sequence = r.Sequence,
                Reference = r.Reference,
                Status = r.Status.ToString(),
                Amount = r.Amount,
                AmountInMinorUnits = r.AmountInMinorUnits,
                Currency = r.Currency,
                Reason = r.Reason,
                PaymentPspReference = r.PaymentPspReference,
                PspReference = r.PspReference,
                ErrorCode = r.ErrorCode,
                ErrorMessage = r.ErrorMessage,
                CreatedAt = r.CreatedAt,
                CompletedAt = r.CompletedAt,
                AdyenResponses = ToDtos(responses.Where(x =>
                    x.Operation == PaymentProviderResponse.RefundOperation && x.RefundSequence == r.Sequence))
            }).ToList()
        };
        return Results.Ok(response);
    }

    private static List<AdyenResponseDto> ToDtos(IEnumerable<PaymentProviderResponse> responses) =>
        responses.Select(r =>
        {
            var dto = new AdyenResponseDto { ReceivedAt = r.ReceivedAt, HttpStatus = r.HttpStatus, Note = r.Note };
            if (!string.IsNullOrEmpty(r.Body))
            {
                try
                {
                    using var document = JsonDocument.Parse(r.Body);
                    dto.Body = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    dto.RawBody = r.Body;
                }
            }
            return dto;
        }).ToList();
}
