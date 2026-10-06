using Microsoft.AspNetCore.DataProtection;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>Encrypts OAuth tokens before they are stored (ASP.NET Core Data Protection).</summary>
public sealed class SquareTokenProtector
{
    private readonly IDataProtector _protector;

    public SquareTokenProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("eShopWeb.SquareIntegration.OAuthTokens.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}
