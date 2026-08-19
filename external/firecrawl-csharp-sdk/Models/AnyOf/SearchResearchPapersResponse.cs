using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(SearchResearchPapersResponseConverter))]
public record SearchResearchPapersResponse
{
    private readonly Optional<ResearchPaperMetadataResponse> _researchPaperMetadataResponseValue;

    private readonly Optional<ResearchReadPaperResponse> _researchReadPaperResponseValue;

    private SearchResearchPapersResponse(Optional<ResearchPaperMetadataResponse> researchPaperMetadataResponseValue,
        Optional<ResearchReadPaperResponse> researchReadPaperResponseValue)
    {
        _researchPaperMetadataResponseValue = researchPaperMetadataResponseValue;
        _researchReadPaperResponseValue = researchReadPaperResponseValue;
    }

    public static SearchResearchPapersResponse ResearchPaperMetadataResponse(ResearchPaperMetadataResponse value) =>
        new(Optional<ResearchPaperMetadataResponse>.Some(value), default);

    public static SearchResearchPapersResponse ResearchReadPaperResponse(ResearchReadPaperResponse value) =>
        new(default, Optional<ResearchReadPaperResponse>.Some(value));

    public bool TryGetResearchPaperMetadataResponse(out ResearchPaperMetadataResponse value) =>
        _researchPaperMetadataResponseValue.TryGetValue(out value);

    public bool TryGetResearchReadPaperResponse(out ResearchReadPaperResponse value) =>
        _researchReadPaperResponseValue.TryGetValue(out value);

    public static implicit operator SearchResearchPapersResponse(ResearchPaperMetadataResponse value) =>
        ResearchPaperMetadataResponse(value);

    public static implicit operator SearchResearchPapersResponse(ResearchReadPaperResponse value) =>
        ResearchReadPaperResponse(value);
}

file sealed class SearchResearchPapersResponseConverter : JsonConverter<SearchResearchPapersResponse>
{
    public override SearchResearchPapersResponse Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ResearchPaperMetadataResponse>(root,
            options,
            out var researchPaperMetadataResponseValue))
        {
            return SearchResearchPapersResponse.ResearchPaperMetadataResponse(researchPaperMetadataResponseValue);
        }
        if (JsonSerializer.TryDeserialize<ResearchReadPaperResponse>(root,
            options,
            out var researchReadPaperResponseValue))
        {
            return SearchResearchPapersResponse.ResearchReadPaperResponse(researchReadPaperResponseValue);
        }
        throw new JsonException($"JSON does not match ResearchPaperMetadataResponse or ResearchReadPaperResponse schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer,
        SearchResearchPapersResponse value,
        JsonSerializerOptions options)
    {
        if (value.TryGetResearchPaperMetadataResponse(out var researchPaperMetadataResponseValue))
        {
            JsonSerializer.Serialize(writer, researchPaperMetadataResponseValue, options);
        }
        else if (value.TryGetResearchReadPaperResponse(out var researchReadPaperResponseValue))
        {
            JsonSerializer.Serialize(writer, researchReadPaperResponseValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(SearchResearchPapersResponse)} contains no valid value to serialize.");
        }
    }
}
