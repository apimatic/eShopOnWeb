using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.AuthEndpoints;

[TestClass]
public class AuthenticateTokenClaimsTest
{
    [TestMethod]
    public async Task SuccessfulTokenContainsImmutableUserIdentifier()
    {
        var request = new AuthenticateRequest
        {
            Username = "demouser@microsoft.com",
            Password = AuthorizationConstants.DEFAULT_PASSWORD
        };
        var response = await ProgramTest.NewClient.PostAsync(
            "api/authenticate",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadAsStringAsync()).FromJson<AuthenticateResponse>();

        var token = new JwtSecurityTokenHandler().ReadJwtToken(body!.Token);

        Assert.IsTrue(token.Claims.Any(claim =>
            (claim.Type == ClaimTypes.NameIdentifier || claim.Type == JwtRegisteredClaimNames.Sub || claim.Type == "nameid") &&
            !string.IsNullOrWhiteSpace(claim.Value)));
    }
}
