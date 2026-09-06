using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Mirrors the spec's Create Subscription schema (the subset this integration sends).</summary>
[MaxioSchema("Create-Subscription")]
public class MaxioCreateSubscription
{
    /// <summary>Handle of the product being subscribed to; preferred over the unstable numeric id.</summary>
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>Id of an existing Maxio customer to enroll.</summary>
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    /// <summary>The reference value (provided by eShopOnWeb) for the subscription itself.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>See the spec's Collection-Method schema: automatic, remittance, prepaid or invoice.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
