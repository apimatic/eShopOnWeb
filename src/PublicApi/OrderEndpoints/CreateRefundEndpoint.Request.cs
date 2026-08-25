namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundRequest : BaseRequest
{
    internal int OrderId { get; set; }

    /// <summary>Caller-supplied idempotency key. Same key = same refund returned.</summary>
    public string IdempotencyKey { get; set; } = "";

    /// <summary>Partial refund amount. Null = full remaining refund.</summary>
    public decimal? Amount { get; set; }

    public string? Currency { get; set; }
}
