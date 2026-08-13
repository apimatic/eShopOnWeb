using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Mints bearer tokens with unique identities so each test's shopper/order/notification data is
/// disjoint from every other test's (the in-memory store is shared across hosts in one test run).
/// </summary>
internal static class TestTokens
{
    public static string NewShopper() => Create($"shopper-{Guid.NewGuid():N}@test.example");

    public static string NewAdmin() => Create($"admin-{Guid.NewGuid():N}@test.example", "Administrators");

    private static string Create(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims.ToArray()),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(tokenDescriptor));
    }
}
