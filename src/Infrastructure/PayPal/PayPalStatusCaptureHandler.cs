using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Captures the last HTTP status so a <see cref="System.Text.Json.JsonException"/> from the SDK
/// can be mapped as a 2xx parse failure or a rejected error body (see dotnet-error-handling).
/// Does not log request or response bodies (they may contain card data).
/// </summary>
public sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> Status = new();

    public static HttpStatusCode? LastStatus => Status.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Status.Value = response.StatusCode;
        return response;
    }
}
