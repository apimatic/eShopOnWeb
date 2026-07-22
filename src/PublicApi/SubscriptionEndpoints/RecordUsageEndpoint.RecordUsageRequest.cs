using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>
    /// Target subscription. Omit to report against the caller's own; supplying it requires the
    /// Administrators role.
    /// </summary>
    public int? SubscriptionId { get; set; }

    [Range(typeof(decimal), "0.00000001", "79228162514264337593543950335")]
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }

    /// <summary>Taken from the bearer token, not from the caller's payload.</summary>
    internal string? UserName { get; set; }

    /// <summary>Taken from the bearer token, not from the caller's payload.</summary>
    internal bool IsAdministrator { get; set; }
}
