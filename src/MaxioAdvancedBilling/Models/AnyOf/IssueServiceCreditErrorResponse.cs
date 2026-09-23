using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(IssueServiceCreditErrorResponseConverter))]
public record IssueServiceCreditErrorResponse
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private readonly Optional<ErrorStringMapResponse1> _errorStringMapResponse1Value;

    private IssueServiceCreditErrorResponse(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<ErrorStringMapResponse1> errorStringMapResponse1Value)
    {
        _errorListResponse1Value = errorListResponse1Value;
        _errorStringMapResponse1Value = errorStringMapResponse1Value;
    }

    public static IssueServiceCreditErrorResponse ErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    public static IssueServiceCreditErrorResponse ErrorStringMapResponse1(ErrorStringMapResponse1 value) =>
        new(default, Optional<ErrorStringMapResponse1>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    public bool TryGetErrorStringMapResponse1(out ErrorStringMapResponse1 value) =>
        _errorStringMapResponse1Value.TryGetValue(out value);

    public static implicit operator IssueServiceCreditErrorResponse(ErrorListResponse1 value) =>
        ErrorListResponse1(value);

    public static implicit operator IssueServiceCreditErrorResponse(ErrorStringMapResponse1 value) =>
        ErrorStringMapResponse1(value);
}

file sealed class IssueServiceCreditErrorResponseConverter : JsonConverter<IssueServiceCreditErrorResponse>
{
    public override IssueServiceCreditErrorResponse Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ErrorListResponse1>(root, options, out var errorListResponse1Value))
        {
            return IssueServiceCreditErrorResponse.ErrorListResponse1(errorListResponse1Value);
        }
        if (JsonSerializer.TryDeserialize<ErrorStringMapResponse1>(root,
            options,
            out var errorStringMapResponse1Value))
        {
            return IssueServiceCreditErrorResponse.ErrorStringMapResponse1(errorStringMapResponse1Value);
        }
        throw new JsonException($"JSON does not match ErrorListResponse1 or ErrorStringMapResponse1 schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer,
        IssueServiceCreditErrorResponse value,
        JsonSerializerOptions options)
    {
        if (value.TryGetErrorListResponse1(out var errorListResponse1Value))
        {
            JsonSerializer.Serialize(writer, errorListResponse1Value, options);
        }
        else if (value.TryGetErrorStringMapResponse1(out var errorStringMapResponse1Value))
        {
            JsonSerializer.Serialize(writer, errorStringMapResponse1Value, options);
        }
        else
        {
            throw new JsonException($"{nameof(IssueServiceCreditErrorResponse)} contains no valid value to serialize.");
        }
    }
}
