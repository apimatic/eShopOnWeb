using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RegisterContactNumberRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    /// <summary>The identifier of the number that was registered (top-level, so callers can drive the flow).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical form of the number.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
