using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local correlation record for a customer that is mastered by Maxio.
/// </summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
