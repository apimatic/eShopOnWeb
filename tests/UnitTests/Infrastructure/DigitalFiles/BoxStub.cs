using System.Net;
using System.Text;
using BoxPlatformApi;
using Microsoft.eShopWeb.Infrastructure.DigitalFiles.Box;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.DigitalFiles;

/// <summary>
/// A fake Box behind the real SDK client: requests go through the production DI registration
/// (<see cref="BoxServiceCollectionExtensions.AddBoxDigitalFiles"/>) into this handler instead of the network.
/// </summary>
public sealed class BoxStub : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public BoxStub(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    public BoxStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = await _responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static ServiceProvider BuildServices(BoxStub stub, Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Box:AccessToken"] = "unit-test-token",
            ["Box:MaxRetries"] = "0",
            ["Box:StallTimeoutSeconds"] = "1",
        };
        foreach (var (key, value) in settings ?? new())
            values[key] = value;

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();
        services.AddBoxDigitalFiles(configuration);
        services.AddHttpClient(BoxServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider();
    }

    public static BoxDigitalFileProvider CreateProvider(BoxStub stub, Dictionary<string, string?>? settings = null) =>
        (BoxDigitalFileProvider)BuildServices(stub, settings).GetRequiredService<Microsoft.eShopWeb.ApplicationCore.Interfaces.IDigitalFileProvider>();
}
