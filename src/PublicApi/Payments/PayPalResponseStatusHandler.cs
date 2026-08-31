using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalResponseStatusContext
{
    private readonly AsyncLocal<HttpStatusCode?> _lastStatus = new();
    public HttpStatusCode? LastStatus { get => _lastStatus.Value; set => _lastStatus.Value = value; }
}

public sealed class PayPalResponseStatusHandler : DelegatingHandler
{
    private readonly PayPalResponseStatusContext _context;

    public PayPalResponseStatusHandler(PayPalResponseStatusContext context) => _context = context;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        _context.LastStatus = response.StatusCode;
        return response;
    }
}
