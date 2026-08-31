using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions over a date range and
/// lines them up against eShop orders, surfacing records only one side knows about.
/// Covers the whole range (all pages), not just the first page.
/// </summary>
public class GetReconciliationEndpoint : EndpointBaseAsync
    .WithRequest<GetReconciliationRequest>
    .WithActionResult<GetReconciliationResponse>
{
    private readonly IReconciliationService _reconciliationService;

    public GetReconciliationEndpoint(IReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Reconciles PayPal transactions against eShop orders",
        Description = "Operator-only. from/to are ISO-8601 date-times; the maximum range PayPal supports is 31 days.",
        OperationId = "reconciliation.get",
        Tags = new[] { "ReconciliationEndpoints" })
    ]
    public override async Task<ActionResult<GetReconciliationResponse>> HandleAsync([FromQuery] GetReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.To <= request.From)
        {
            return BadRequest("'to' must be after 'from'.");
        }
        if (request.To - request.From > TimeSpan.FromDays(31))
        {
            return BadRequest("PayPal supports a maximum reconciliation range of 31 days.");
        }

        var report = await _reconciliationService.GetReconciliationAsync(request.From, request.To, cancellationToken);

        return new GetReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Transactions = report.Transactions.Select(Map).ToList(),
            UnmatchedPayPalTransactions = report.UnmatchedProcessorTransactions.Select(Map).ToList(),
            PaymentsMissingFromPayPal = report.PaymentsMissingFromProcessor.Select(m => new MissingPaymentDto
            {
                OrderId = m.OrderId,
                PaymentId = m.PaymentId,
                ExpectedRecordType = m.ExpectedRecordType,
                ExpectedTransactionId = m.ExpectedTransactionId,
                PaymentStatus = m.PaymentStatus
            }).ToList()
        };
    }

    private static ReconciliationTransactionDto Map(ReconciledTransaction t) => new()
    {
        TransactionId = t.TransactionId,
        ReferenceId = t.ReferenceId,
        EventCode = t.EventCode,
        Status = t.Status,
        Amount = t.Amount,
        Currency = t.Currency,
        Fee = t.Fee,
        TransactionDate = t.TransactionDate,
        OrderId = t.OrderId,
        PaymentId = t.PaymentId,
        MatchType = t.MatchType
    };
}

public class GetReconciliationRequest : BaseRequest
{
    /// <summary>Start of the range, ISO-8601 date-time.</summary>
    [FromQuery(Name = "from")]
    public DateTimeOffset From { get; set; }

    /// <summary>End of the range, ISO-8601 date-time.</summary>
    [FromQuery(Name = "to")]
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<ReconciliationTransactionDto> UnmatchedPayPalTransactions { get; set; } = new();
    public List<MissingPaymentDto> PaymentsMissingFromPayPal { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }

    /// <summary>Which eShop record this transaction matched: Authorization, Capture, Refund, or null.</summary>
    public string? MatchType { get; set; }
}

public class MissingPaymentDto
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string ExpectedRecordType { get; set; } = string.Empty;
    public string ExpectedTransactionId { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}
