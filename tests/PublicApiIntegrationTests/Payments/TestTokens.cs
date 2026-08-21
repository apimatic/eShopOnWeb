using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.Payments;

/// <summary>Forges JWTs signed with the app's shared key, for arbitrary test identities.</summary>
public static class TestTokens
{
    public static string ForShopper(string userName) => Create(userName, Array.Empty<string>());

    public static string ForAdmin(string userName) => Create(userName, new[] { "Administrators" });

    private static string Create(string userName, string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims.ToArray()),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
