namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class MaxioCustomerLink
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
}
