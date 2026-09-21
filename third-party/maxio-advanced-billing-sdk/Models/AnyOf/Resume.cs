using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

/// <summary>
/// If <c>true</c>, Advanced Billing will attempt to resume the subscription's billing period. If not resumable, the subscription will be reactivated with a new billing period. If <c>false</c> or omitted, Advanced Billing will only attempt to reactivate the subscription with a new billing period, regardless of whether or not the subscription is resumable.
/// </summary>
[JsonConverter(typeof(ResumeConverter))]
public record Resume
{
    private readonly Optional<bool> _boolValue;

    private readonly Optional<ResumeOptions> _resumeOptionsValue;

    private Resume(Optional<bool> boolValue, Optional<ResumeOptions> resumeOptionsValue)
    {
        _boolValue = boolValue;
        _resumeOptionsValue = resumeOptionsValue;
    }

    public static Resume Bool(bool value) => new(Optional<bool>.Some(value), default);

    public static Resume ResumeOptions(ResumeOptions value) =>
        new(default, Optional<ResumeOptions>.Some(value));

    public bool TryGetBool(out bool value) => _boolValue.TryGetValue(out value);

    public bool TryGetResumeOptions(out ResumeOptions value) => _resumeOptionsValue.TryGetValue(out value);

    public static implicit operator Resume(bool value) => Bool(value);

    public static implicit operator Resume(ResumeOptions value) => ResumeOptions(value);
}

file sealed class ResumeConverter : JsonConverter<Resume>
{
    public override Resume Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<bool>(root, options, out var boolValue))
        {
            return Resume.Bool(boolValue);
        }
        if (JsonSerializer.TryDeserialize<ResumeOptions>(root, options, out var resumeOptionsValue))
        {
            return Resume.ResumeOptions(resumeOptionsValue);
        }
        throw new JsonException($"JSON does not match bool or ResumeOptions schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Resume value, JsonSerializerOptions options)
    {
        if (value.TryGetBool(out var boolValue))
        {
            JsonSerializer.Serialize(writer, boolValue, options);
        }
        else if (value.TryGetResumeOptions(out var resumeOptionsValue))
        {
            JsonSerializer.Serialize(writer, resumeOptionsValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Resume)} contains no valid value to serialize.");
        }
    }
}
