using System;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio.Models;

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomerRequest
{
    public MaxioCustomerDraft? Customer { get; set; }
}

public class MaxioCustomerDraft
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}
