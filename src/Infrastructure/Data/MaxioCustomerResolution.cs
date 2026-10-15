namespace Microsoft.eShopWeb.Infrastructure.Data;

public class MaxioCustomerResolution
{
    public string UserName { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string Status { get; set; } = "Pending";
}
