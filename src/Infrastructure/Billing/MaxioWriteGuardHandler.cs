using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private readonly MaxioRequestContext _context;

    public MaxioWriteGuardHandler(MaxioRequestContext context) => _context = context;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _context.BeforeSend(request.Method == HttpMethod.Post);
        var response = await base.SendAsync(request, cancellationToken);
        _context.RecordResponse(response.StatusCode);
        return response;
    }
}
