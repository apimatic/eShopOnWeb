using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
