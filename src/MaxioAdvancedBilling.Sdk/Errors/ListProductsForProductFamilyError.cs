using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListProductsForProductFamilyError : ApiError
{
    private readonly Optional<string> _stringValue;

    private ListProductsForProductFamilyError(Optional<string> stringValue, Optional<RawError> fallback) : base(fallback)
    {
        _stringValue = stringValue;
    }

    private static ListProductsForProductFamilyError AsString(string value) =>
        new(Optional<string>.Some(value), default);

    private static ListProductsForProductFamilyError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    internal static Task<ListProductsForProductFamilyError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<string>(response, ct).As(AsString),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListProductsForProductFamilyErrorResponse : IErrorResponse<ListProductsForProductFamilyError>
{
    public static ListProductsForProductFamilyErrorResponse Instance { get; } = new();

    private ListProductsForProductFamilyErrorResponse()
    {
    }

    public Task<ListProductsForProductFamilyError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListProductsForProductFamilyError.Create(response, ct);
}
