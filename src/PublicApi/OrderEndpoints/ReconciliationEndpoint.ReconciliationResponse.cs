using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalTransactions { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ReconciliationUnmatchedOrderDto> UnmatchedOrders { get; set; } = new List<ReconciliationUnmatchedOrderDto>();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset InitiationDate { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
}

public class ReconciliationUnmatchedOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset PaymentDate { get; set; }
}

