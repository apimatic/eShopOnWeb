using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.eShopWeb.PaymentsTests.Fakes;

/// <summary>
/// Hosts the real PublicApi with the real PayPal SDK client, whose HttpClient talks to
/// <see cref="FakePayPal"/> instead of the network. Each factory gets its own in-memory database.
/// </summary>
public sealed class PaymentsApiFactory : WebApplicationFactory<Program>
{
    public FakePayPal PayPal { get; } = new();
    public OffsetTimeProvider Clock { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public PayPalResilienceSettings Resilience { get; } = new()
    {
        RequestBudget = TimeSpan.FromSeconds(25),
        SettlementReserve = TimeSpan.FromSeconds(6),
        AttemptTimeout = TimeSpan.FromSeconds(10),
        HttpClientTimeout = TimeSpan.FromSeconds(15),
        MaxReadRetries = 0,
    };

    private readonly string _databaseName = "payments-tests-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Logs);
            logging.SetMinimumLevel(LogLevel.Trace);
            // Capture everything, whatever the app's "Logging" section filters for other providers.
            logging.AddFilter<CapturingLoggerProvider>(category: null, LogLevel.Trace);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(_databaseName));
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase(_databaseName + "-identity"));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<PayPalResilienceSettings>();
            services.AddSingleton(Resilience);

            services.AddHttpClient(PayPalServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => PayPal);
        });
    }

    public HttpClient ClientFor(string userName, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userName, roles));
        return client;
    }

    public static string Token(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY)), SecurityAlgorithms.HmacSha256Signature),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}

/// <summary>System time shifted by an adjustable offset (timers stay real).</summary>
public sealed class OffsetTimeProvider : TimeProvider
{
    public TimeSpan Offset { get; set; }
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    public string All => string.Join("\n", Messages);

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Messages.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}

public static class ApiCalls
{
    public const string Shipping = """{"street":"1 Main St","city":"San Jose","state":"CA","country":"US","zipCode":"95131"}""";
    public const string TestVisa = "4111111111111111";

    public static object Card(string number = TestVisa) => new
    {
        number,
        expiry = "2030-12",
        securityCode = "123",
        name = "Test Shopper",
        billingAddress = new { addressLine1 = "1 Main St", city = "San Jose", state = "CA", postalCode = "95131", countryCode = "US" },
    };

    public static async Task<int> PlaceOrderAsync(this HttpClient client, params (int CatalogItemId, int Quantity)[] items)
    {
        var body = $$"""{"items":[{{string.Join(",", items.Select(i => $$"""{"catalogItemId":{{i.CatalogItemId}},"quantity":{{i.Quantity}}}"""))}}],"shipToAddress":{{Shipping}}}""";
        var response = await client.PostAsync("api/orders", new StringContent(body, Encoding.UTF8, "application/json"));
        var json = await response.ReadJsonAsync();
        Assert.True(response.IsSuccessStatusCode, json.ToJsonString());
        return json["orderId"]!.GetValue<int>();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object? body) =>
        client.PostAsJsonAsync(url, body ?? new { });

    public static async Task<JsonNode> ReadJsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? new JsonObject() : JsonNode.Parse(text)!;
    }

    public static decimal Dec(this JsonNode? node) => node!.GetValue<decimal>();
}
