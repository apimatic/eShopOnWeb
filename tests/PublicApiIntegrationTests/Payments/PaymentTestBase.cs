using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

public abstract class PaymentTestBase
{
    protected PaymentApiFactory Factory = null!;

    [TestInitialize]
    public void Init() => Factory = new PaymentApiFactory();

    [TestCleanup]
    public void Cleanup() => Factory.Dispose();

    protected static string DemoToken => ApiTokenHelper.GetNormalUserToken();   // demouser@microsoft.com
    protected static string OtherToken => ApiTokenHelper.GetAdminUserToken();   // admin@microsoft.com (a different user)

    /// <summary>
    /// A bearer token for an arbitrary username. The in-memory catalog store is shared across the
    /// process, so tests that assert absolute counts use a unique username to stay isolated.
    /// </summary>
    protected static string TokenFor(string userName)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, userName) };
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

    protected static string UniqueUserToken() => TokenFor("user-" + Guid.NewGuid());

    protected HttpClient AnonymousClient() => Factory.CreateClient();

    protected HttpClient AuthedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    protected async Task<int> CreateOrderAsync(HttpClient client, int catalogItemId = 2, int quantity = 1)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId, quantity } }
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("orderId").GetInt32();
    }

    protected static object CardBody(int expiryMonth = 12, int expiryYear = 2030) => new
    {
        card = new
        {
            cardholderName = "Test Shopper",
            number = "4111111111111111",
            expiryMonth,
            expiryYear,
            securityCode = "123",
            billingAddress = new
            {
                addressLine1 = "1 Market St",
                city = "San Francisco",
                state = "CA",
                postalCode = "94105",
                countryCode = "US"
            }
        }
    };

    protected static object SaveCardBody(int expiryMonth = 11, int expiryYear = 2031, string alias = "My Visa") => new
    {
        alias,
        card = new
        {
            cardholderName = "Test Shopper",
            number = "4111111111111111",
            expiryMonth,
            expiryYear,
            securityCode = "123",
            billingAddress = new
            {
                addressLine1 = "1 Market St",
                city = "San Francisco",
                state = "CA",
                postalCode = "94105",
                countryCode = "US"
            }
        }
    };
}
