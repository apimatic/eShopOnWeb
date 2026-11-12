namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A claim on a billing write, taken before the Maxio call is made. The primary key (Key) is what
/// refuses a second caller asking for the same operation.
/// </summary>
public class MaxioOperationClaim
{
    public string Key { get; set; } = string.Empty;
    public System.DateTime ClaimedAtUtc { get; set; }
    public string? Result { get; set; }
}
