using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Models.AnyOf;

[JsonConverter(typeof(CategoryConverter))]
public record Category
{
    private readonly Optional<GitHub> _gitHubValue;

    private readonly Optional<Research> _researchValue;

    private readonly Optional<Pdf> _pdfValue;

    private Category(Optional<GitHub> gitHubValue, Optional<Research> researchValue, Optional<Pdf> pdfValue)
    {
        _gitHubValue = gitHubValue;
        _researchValue = researchValue;
        _pdfValue = pdfValue;
    }

    public static Category GitHub(GitHub value) => new(Optional<GitHub>.Some(value), default, default);

    public static Category Research(Research value) =>
        new(default, Optional<Research>.Some(value), default);

    public static Category Pdf(Pdf value) => new(default, default, Optional<Pdf>.Some(value));

    public bool TryGetGitHub(out GitHub value) => _gitHubValue.TryGetValue(out value);

    public bool TryGetResearch(out Research value) => _researchValue.TryGetValue(out value);

    public bool TryGetPdf(out Pdf value) => _pdfValue.TryGetValue(out value);

    public static implicit operator Category(GitHub value) => GitHub(value);

    public static implicit operator Category(Research value) => Research(value);

    public static implicit operator Category(Pdf value) => Pdf(value);
}

file sealed class CategoryConverter : JsonConverter<Category>
{
    public override Category Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<GitHub>(root, options, out var gitHubValue))
        {
            return Category.GitHub(gitHubValue);
        }
        if (JsonSerializer.TryDeserialize<Research>(root, options, out var researchValue))
        {
            return Category.Research(researchValue);
        }
        if (JsonSerializer.TryDeserialize<Pdf>(root, options, out var pdfValue))
        {
            return Category.Pdf(pdfValue);
        }
        throw new JsonException($"JSON does not match GitHub or Research or Pdf schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Category value, JsonSerializerOptions options)
    {
        if (value.TryGetGitHub(out var gitHubValue))
        {
            JsonSerializer.Serialize(writer, gitHubValue, options);
        }
        else if (value.TryGetResearch(out var researchValue))
        {
            JsonSerializer.Serialize(writer, researchValue, options);
        }
        else if (value.TryGetPdf(out var pdfValue))
        {
            JsonSerializer.Serialize(writer, pdfValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Category)} contains no valid value to serialize.");
        }
    }
}
