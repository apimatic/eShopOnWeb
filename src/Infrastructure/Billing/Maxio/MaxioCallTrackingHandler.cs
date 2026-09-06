using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Runs inside every Maxio HTTP attempt (the SDK's retry pipeline wraps <c>SendAsync</c>, so this handler
/// sees each attempt individually). It enforces the write-once guarantee and records the response status
/// for the ambient <see cref="MaxioCallScope"/>.
/// </summary>
internal sealed class MaxioCallTrackingHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioCallTrackingHandler> _logger;

    public MaxioCallTrackingHandler(IOptionsMonitor<MaxioOptions> options, ILogger<MaxioCallTrackingHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = MaxioCallScope.Current;

        if (scope is not null && !scope.TryRegisterSend())
        {
            _logger.LogWarning(
                "Blocked attempt {Attempt} of write-once Maxio operation {Operation} ({Method} {Uri}).",
                scope.Sends, scope.Operation, request.Method, request.RequestUri);

            throw new MaxioResendBlockedException(scope.Operation);
        }

        if (_options.CurrentValue.LogRequests)
        {
            _logger.LogInformation("--> Maxio {Method} {Uri}", request.Method, request.RequestUri);
        }

        var response = await base.SendAsync(request, cancellationToken);

        scope?.RecordStatus(response.StatusCode);

        if (_options.CurrentValue.LogRequests)
        {
            _logger.LogInformation("<-- Maxio {StatusCode} {Method} {Uri}", (int)response.StatusCode, request.Method, request.RequestUri);
        }

        return response;
    }
}
