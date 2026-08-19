using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetCreditUsageError : ApiError
{
    private readonly Optional<TeamCreditUsage404Error1> _teamCreditUsage404Error1Value;

    private readonly Optional<TeamCreditUsage500Error1> _teamCreditUsage500Error1Value;

    private GetCreditUsageError(Optional<TeamCreditUsage404Error1> teamCreditUsage404Error1Value,
        Optional<TeamCreditUsage500Error1> teamCreditUsage500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _teamCreditUsage404Error1Value = teamCreditUsage404Error1Value;
        _teamCreditUsage500Error1Value = teamCreditUsage500Error1Value;
    }

    private static GetCreditUsageError AsTeamCreditUsage404Error1(TeamCreditUsage404Error1 value) =>
        new(Optional<TeamCreditUsage404Error1>.Some(value), default, default);

    private static GetCreditUsageError AsTeamCreditUsage500Error1(TeamCreditUsage500Error1 value) =>
        new(default, Optional<TeamCreditUsage500Error1>.Some(value), default);

    private static GetCreditUsageError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetTeamCreditUsage404Error1(out TeamCreditUsage404Error1 value) =>
        _teamCreditUsage404Error1Value.TryGetValue(out value);

    public bool TryGetTeamCreditUsage500Error1(out TeamCreditUsage500Error1 value) =>
        _teamCreditUsage500Error1Value.TryGetValue(out value);

    internal static Task<GetCreditUsageError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<TeamCreditUsage404Error1>(response, ct).As(AsTeamCreditUsage404Error1),
            500 => FromJson<TeamCreditUsage500Error1>(response, ct).As(AsTeamCreditUsage500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetCreditUsageErrorResponse : IErrorResponse<GetCreditUsageError>
{
    public static GetCreditUsageErrorResponse Instance { get; } = new();

    private GetCreditUsageErrorResponse()
    {
    }

    public Task<GetCreditUsageError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetCreditUsageError.Create(response, ct);
}
