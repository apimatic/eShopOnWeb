namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class CustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class CreateCustomerRequest
{
    public CustomerAttributes? Customer { get; set; }
}

public class CustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}
