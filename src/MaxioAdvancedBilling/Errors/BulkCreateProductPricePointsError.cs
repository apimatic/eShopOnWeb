using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class BulkCreateProductPricePointsError : ApiError
{
    private readonly Optional<IReadOnlyDictionary<string, JsonElement>> _mapOfJsonElementValue;

    private BulkCreateProductPricePointsError(Optional<IReadOnlyDictionary<string, JsonElement>> mapOfJsonElementValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _mapOfJsonElementValue = mapOfJsonElementValue;
    }

    private static BulkCreateProductPricePointsError AsMapOfJsonElement(IReadOnlyDictionary<string, JsonElement> value) =>
        new(Optional<IReadOnlyDictionary<string, JsonElement>>.Some(value), default);

    private static BulkCreateProductPricePointsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetMapOfJsonElement(out IReadOnlyDictionary<string, JsonElement> value) =>
        _mapOfJsonElementValue.TryGetValue(out value);

    internal static Task<BulkCreateProductPricePointsError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<IReadOnlyDictionary<string, JsonElement>>(response, ct).As(AsMapOfJsonElement),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class BulkCreateProductPricePointsErrorResponse : IErrorResponse<BulkCreateProductPricePointsError>
{
    public static BulkCreateProductPricePointsErrorResponse Instance { get; } = new();

    private BulkCreateProductPricePointsErrorResponse()
    {
    }

    public Task<BulkCreateProductPricePointsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        BulkCreateProductPricePointsError.Create(response, ct);
}
