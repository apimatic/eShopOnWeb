using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Remembers the last HTTP status seen on this async flow so a <see cref="System.Text.Json.JsonException"/>
/// can be classified as a rejected request vs an unreadable success body.
/// </summary>
public sealed class LastStatusHandler : DelegatingHandler
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
