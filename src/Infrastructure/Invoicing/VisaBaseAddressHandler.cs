using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Forces every outgoing provider request onto the configured <c>Visa:BaseUrl</c>. Whatever absolute
/// address the SDK composes, this handler rewrites the scheme, host and port (and applies any base
/// path from the configured URL) so no provider call can bypass the configured base address or carry
/// a hard-coded host. The request path and query the SDK produced are preserved.
/// </summary>
public sealed class VisaBaseAddressHandler : DelegatingHandler
{
    private readonly Uri _baseUrl;

    public VisaBaseAddressHandler(Uri baseUrl)
    {
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.RequestUri = Rewrite(request.RequestUri);
        return base.SendAsync(request, cancellationToken);
    }

    private Uri Rewrite(Uri? original)
    {
        if (original is null)
        {
            return _baseUrl;
        }

        // Combine any base path from the configured URL with the SDK-produced resource path so a
        // configured URL that includes a path prefix is honoured verbatim.
        var basePath = _baseUrl.AbsolutePath.TrimEnd('/');
        var combinedPath = string.IsNullOrEmpty(basePath)
            ? original.AbsolutePath
            : basePath + original.AbsolutePath;

        var builder = new UriBuilder
        {
            Scheme = _baseUrl.Scheme,
            Host = _baseUrl.Host,
            Port = _baseUrl.IsDefaultPort ? -1 : _baseUrl.Port,
            Path = combinedPath,
            Query = original.Query.TrimStart('?')
        };

        return builder.Uri;
    }
}
