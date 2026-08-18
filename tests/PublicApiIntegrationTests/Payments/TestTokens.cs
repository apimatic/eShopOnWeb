using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.Payments;

/// <summary>Mints JWTs the PublicApi accepts, including tokens for arbitrary shoppers (ownership tests).</summary>
internal static class TestTokens
{
    public const string ShopperA = "shopperA@test.com";
    public const string ShopperB = "shopperB@test.com";

    public static string Shopper(string username) => Create(username, Array.Empty<string>());

    public static string Admin() => Create("admin@microsoft.com", new[] { "Administrators" });

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
