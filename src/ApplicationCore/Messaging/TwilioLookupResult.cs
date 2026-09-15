namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record TwilioLookupResult(bool Valid, string? CanonicalNumber, string?[]? ValidationErrors);
