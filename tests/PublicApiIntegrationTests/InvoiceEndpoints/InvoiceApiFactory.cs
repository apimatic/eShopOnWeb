using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// Test host for the invoicing endpoints: the real PublicApi app with the provider integration swapped
/// for <see cref="FakeInvoicingService"/> so nothing hits the network.
/// </summary>
public class InvoiceApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInvoicingService>();
            services.AddSingleton<FakeInvoicingService>();
            services.AddSingleton<IInvoicingService>(sp => sp.GetRequiredService<FakeInvoicingService>());
        });
    }

    public static string CreateToken(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims.ToArray()),
            Expires = System.DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public static string AdminToken(string userName = "admin@microsoft.com") =>
        CreateToken(userName, BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static string ShopperToken(string userName) => CreateToken(userName);
}
