using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SquareIntegration;

/// <summary>
/// PublicApi host wired to <see cref="FakeSquare"/>: placeholder (non-secret) Square settings, its own
/// in-memory catalog database, and no network access.
/// </summary>
public sealed class SquareApiFactory : WebApplicationFactory<Program>
{
    public const string ApplicationId = "sandbox-sq0idb-test-application";
    public const string ApplicationSecret = "test-application-secret-placeholder";
    public const string RedirectUri = "https://localhost:39943/api/square/callback";

    private readonly string _databaseName = "SquareTests-" + Guid.NewGuid().ToString("N");
    private readonly bool _withAccessToken;

    public SquareApiFactory(bool withAccessToken = true)
    {
        _withAccessToken = withAccessToken;
    }

    public FakeSquare Square { get; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("UseOnlyInMemoryDatabase", "true");
        builder.UseSetting("Square:Environment", "sandbox");
        builder.UseSetting("Square:ApplicationId", ApplicationId);
        builder.UseSetting("Square:ApplicationSecret", ApplicationSecret);
        builder.UseSetting("Square:RedirectUri", RedirectUri);
        builder.UseSetting("Square:AccessToken", _withAccessToken ? FakeSquare.ConfiguredAccessToken : string.Empty);

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped(sp => new DbContextOptionsBuilder<CatalogContext>()
                .UseInMemoryDatabase(_databaseName)
                .UseApplicationServiceProvider(sp)
                .Options);
            services.AddHttpClient(SquareClientFactory.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Square);
        });
    }

    public HttpClient AdminClient() => ClientWith(ApiTokenHelper.GetAdminUserToken());

    public HttpClient ShopperClient() => ClientWith(ApiTokenHelper.GetNormalUserToken());

    public HttpClient ClientWith(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task WithDbAsync(Func<CatalogContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        await action(db);
    }

    public async Task<T> WithDbAsync<T>(Func<CatalogContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        return await action(db);
    }

    public Task<int[]> CatalogItemIdsAsync() =>
        WithDbAsync(db => db.CatalogItems.OrderBy(i => i.Id).Select(i => i.Id).ToArrayAsync());

    public async Task<JsonElement> SyncAsync()
    {
        var response = await AdminClient().PostAsync("api/square/catalog/sync", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }
}

public static class JsonElementExtensions
{
    /// <summary>Property lookup that ignores case (API bodies are camelCase; error details are PascalCase).</summary>
    public static JsonElement Prop(this JsonElement element, string name) =>
        element.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    public static int Int(this JsonElement element, string name) => element.Prop(name).GetInt32();

    public static string? Str(this JsonElement element, string name) =>
        element.EnumerateObject().Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value).FirstOrDefault() is { ValueKind: JsonValueKind.String } value
            ? value.GetString()
            : null;
}
