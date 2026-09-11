namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class CustomerResponse
{
    public Customer Customer { get; set; } = new();
}

public class Customer
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
