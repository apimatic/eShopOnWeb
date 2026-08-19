using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetHistoricalCreditUsageError : ApiError
{
    private readonly Optional<TeamCreditUsageHistorical500Error1> _teamCreditUsageHistorical500Error1Value;

    private GetHistoricalCreditUsageError(Optional<TeamCreditUsageHistorical500Error1> teamCreditUsageHistorical500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _teamCreditUsageHistorical500Error1Value = teamCreditUsageHistorical500Error1Value;
    }

    private static GetHistoricalCreditUsageError AsTeamCreditUsageHistorical500Error1(TeamCreditUsageHistorical500Error1 value) =>
        new(Optional<TeamCreditUsageHistorical500Error1>.Some(value), default);

    private static GetHistoricalCreditUsageError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetTeamCreditUsageHistorical500Error1(out TeamCreditUsageHistorical500Error1 value) =>
        _teamCreditUsageHistorical500Error1Value.TryGetValue(out value);

    internal static Task<GetHistoricalCreditUsageError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            500 => FromJson<TeamCreditUsageHistorical500Error1>(response, ct).As(AsTeamCreditUsageHistorical500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetHistoricalCreditUsageErrorResponse : IErrorResponse<GetHistoricalCreditUsageError>
{
    public static GetHistoricalCreditUsageErrorResponse Instance { get; } = new();

    private GetHistoricalCreditUsageErrorResponse()
    {
    }

    public Task<GetHistoricalCreditUsageError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetHistoricalCreditUsageError.Create(response, ct);
}
