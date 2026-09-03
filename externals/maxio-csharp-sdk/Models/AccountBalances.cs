using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record AccountBalances
{
    /// <summary>
    /// The balance, in cents, of the sum of the subscription's open, payable invoices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("open_invoices")]
    public AccountBalance? OpenInvoices { get; init; }

    /// <summary>
    /// The balance, in cents, of the sum of the subscription's pending, payable invoices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pending_invoices")]
    public AccountBalance? PendingInvoices { get; init; }

    /// <summary>
    /// The balance, in cents, of the subscription's Pending Discount account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pending_discounts")]
    public AccountBalance? PendingDiscounts { get; init; }

    /// <summary>
    /// The balance, in cents, of the subscription's Service Credit account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("service_credits")]
    public AccountBalance? ServiceCredits { get; init; }

    /// <summary>
    /// The balance, in cents, of the subscription's Prepayment account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prepayments")]
    public AccountBalance? Prepayments { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
