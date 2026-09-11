namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class CustomerResponse
{
    public int id { get; set; }
    public string reference { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string first_name { get; set; } = string.Empty;
    public string last_name { get; set; } = string.Empty;
}
