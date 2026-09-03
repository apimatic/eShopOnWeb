using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Models;
using Twilio.Models;

namespace Twilio.Errors;

public sealed class CreateV3TypingIndicatorError : ApiError
{
    private readonly Optional<AccountsCallsRecordingsSidJson201041408Error1> _accountsCallsRecordingsSidJson201041408Error1Value;

    private CreateV3TypingIndicatorError(Optional<AccountsCallsRecordingsSidJson201041408Error1> accountsCallsRecordingsSidJson201041408Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _accountsCallsRecordingsSidJson201041408Error1Value = accountsCallsRecordingsSidJson201041408Error1Value;
    }

    private static CreateV3TypingIndicatorError AsAccountsCallsRecordingsSidJson201041408Error1(AccountsCallsRecordingsSidJson201041408Error1 value) =>
        new(Optional<AccountsCallsRecordingsSidJson201041408Error1>.Some(value), default);

    private static CreateV3TypingIndicatorError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetAccountsCallsRecordingsSidJson201041408Error1(out AccountsCallsRecordingsSidJson201041408Error1 value) =>
        _accountsCallsRecordingsSidJson201041408Error1Value.TryGetValue(out value);

    internal static Task<CreateV3TypingIndicatorError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 => FromJson<AccountsCallsRecordingsSidJson201041408Error1>(response, ct).As(AsAccountsCallsRecordingsSidJson201041408Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateV3TypingIndicatorErrorResponse : IErrorResponse<CreateV3TypingIndicatorError>
{
    public static CreateV3TypingIndicatorErrorResponse Instance { get; } = new();

    private CreateV3TypingIndicatorErrorResponse()
    {
    }

    public Task<CreateV3TypingIndicatorError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateV3TypingIndicatorError.Create(response, ct);
}
