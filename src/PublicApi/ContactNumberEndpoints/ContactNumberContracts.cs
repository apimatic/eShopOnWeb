using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Register a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>The created contact number; <c>contactNumberId</c> is the top-level identifier.</summary>
public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListContactNumbersResponse
{
    public IReadOnlyList<ContactNumberView> ContactNumbers { get; set; } = new List<ContactNumberView>();
}
