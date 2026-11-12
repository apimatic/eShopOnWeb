using System;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

[TestClass]
public class MaxioSettingsTest
{
    [TestMethod]
    public void BlankApiKeyIsRefused()
    {
        Assert.ThrowsException<InvalidOperationException>(() => new MaxioSettings { ApiKey = " ", Subdomain = "site" }.Validate());
    }

    [TestMethod]
    public void BlankSubdomainIsRefusedUnlessABaseUrlIsGiven()
    {
        Assert.ThrowsException<InvalidOperationException>(() => new MaxioSettings { ApiKey = "k", Subdomain = "" }.Validate());
        new MaxioSettings { ApiKey = "k", BaseUrl = "https://billing.example.test" }.Validate();
    }

    [TestMethod]
    public void BaseUrlMustBeAnAbsoluteHttpUrl()
    {
        Assert.ThrowsException<InvalidOperationException>(() => new MaxioSettings { ApiKey = "k", BaseUrl = "not a url" }.Validate());
    }

    [TestMethod]
    public void EnvironmentIsUsOrEu()
    {
        Assert.AreEqual(MaxioAdvancedBilling.Servers.ServerEnvironment.Us, new MaxioSettings { Environment = "us" }.ResolveEnvironment());
        Assert.AreEqual(MaxioAdvancedBilling.Servers.ServerEnvironment.Eu, new MaxioSettings { Environment = "EU" }.ResolveEnvironment());
        Assert.AreEqual(MaxioAdvancedBilling.Servers.ServerEnvironment.Us, new MaxioSettings { Environment = null }.ResolveEnvironment());
        Assert.ThrowsException<InvalidOperationException>(() => new MaxioSettings { Environment = "mars" }.ResolveEnvironment());
    }

    [TestMethod]
    public void TheHostRefusesToStartWithoutCredentials()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Maxio:ApiKey", ""));
        Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());
    }

    [TestMethod]
    public async Task BaseUrlIsUsedVerbatimInsteadOfTheSubdomainAddress()
    {
        // Nothing listens here: if the override were ignored the call would reach the real site and succeed.
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Maxio:BaseUrl", "http://127.0.0.1:9"));
        var client = factory.CreateClient();

        var claims = new List<Claim> { new(ClaimTypes.Name, "someone@example.com") };
        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("api/plans");
        Assert.IsTrue(response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout, response.StatusCode.ToString());
    }
}
