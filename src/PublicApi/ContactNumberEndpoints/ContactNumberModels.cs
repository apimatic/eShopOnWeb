using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Body of POST /api/contact-numbers. <see cref="BuyerId"/> is set server-side from the token.</summary>
public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the caller's token; any client-supplied value is ignored.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Response of POST /api/contact-numbers — carries the new id as a top-level field.</summary>
public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public record ListContactNumbersResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

/// <summary>Route + identity for DELETE /api/contact-numbers/{contactNumberId}.</summary>
public record DeleteContactNumberRequest(int ContactNumberId, string BuyerId);
