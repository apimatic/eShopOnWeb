using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioPipelineHandler : DelegatingHandler
{
    private readonly MaxioCallContext _context;

    public MaxioPipelineHandler(MaxioCallContext context) => _context = context;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _context.BeforeSend(request.Method);
        var response = await base.SendAsync(request, cancellationToken);
        _context.RecordStatus(response.StatusCode);
        return response;
    }
}
