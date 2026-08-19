using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class StartAgentError : ApiError
{
    private readonly Optional<Agent402Error1> _agent402Error1Value;

    private readonly Optional<Agent429Error1> _agent429Error1Value;

    private StartAgentError(Optional<Agent402Error1> agent402Error1Value,
        Optional<Agent429Error1> agent429Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _agent402Error1Value = agent402Error1Value;
        _agent429Error1Value = agent429Error1Value;
    }

    private static StartAgentError AsAgent402Error1(Agent402Error1 value) =>
        new(Optional<Agent402Error1>.Some(value), default, default);

    private static StartAgentError AsAgent429Error1(Agent429Error1 value) =>
        new(default, Optional<Agent429Error1>.Some(value), default);

    private static StartAgentError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetAgent402Error1(out Agent402Error1 value) => _agent402Error1Value.TryGetValue(out value);

    public bool TryGetAgent429Error1(out Agent429Error1 value) => _agent429Error1Value.TryGetValue(out value);

    internal static Task<StartAgentError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<Agent402Error1>(response, ct).As(AsAgent402Error1),
            429 => FromJson<Agent429Error1>(response, ct).As(AsAgent429Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class StartAgentErrorResponse : IErrorResponse<StartAgentError>
{
    public static StartAgentErrorResponse Instance { get; } = new();

    private StartAgentErrorResponse()
    {
    }

    public Task<StartAgentError> Map(HttpResponseMessage response, CancellationToken ct) =>
        StartAgentError.Create(response, ct);
}
