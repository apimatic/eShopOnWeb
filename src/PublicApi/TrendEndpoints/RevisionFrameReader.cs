using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using WikimediaEventStreams.Models;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// One page revision read from a <c>mediawiki.revision-create</c> stream frame.
/// </summary>
public sealed record WikiRevision(
    string? Wiki,
    string PageTitle,
    long RevisionId,
    string? Editor,
    DateTimeOffset Timestamp,
    IReadOnlyList<WikiEditSlotDto> Slots);

/// <summary>
/// Reads one stream frame into a <see cref="WikiRevision"/>.
/// The SDK's own <see cref="MediawikiRevisionCreate"/> model is tried first. Live revision ids already exceed
/// that model's 32-bit <c>rev_id</c>, so a frame it rejects is read again, tolerantly, by the same wire names.
/// </summary>
public static class RevisionFrameReader
{
    // The SDK deserializes JSON event frames with the web defaults.
    private static readonly JsonSerializerOptions SdkJsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryRead(string? frame, out WikiRevision? revision)
    {
        revision = null;
        if (string.IsNullOrWhiteSpace(frame))
            return false;

        try
        {
            var model = JsonSerializer.Deserialize<MediawikiRevisionCreate>(frame, SdkJsonOptions);
            if (model is not null)
            {
                revision = FromModel(model);
                return true;
            }
        }
        catch (JsonException)
        {
            // Fall through to the tolerant reader.
        }

        return TryReadTolerant(frame, out revision);
    }

    private static WikiRevision FromModel(MediawikiRevisionCreate model) => new(
        WikiOf(model.Meta.Domain, model.Meta.Uri),
        model.PageTitle,
        model.RevId,
        model.Performer?.UserText,
        model.RevTimestamp,
        SlotsOf(model.RevSlots));

    /// <summary>
    /// "main" plus every other slot the revision carries — including kinds this SDK does not model.
    /// </summary>
    private static List<WikiEditSlotDto> SlotsOf(RevSlots? slots)
    {
        var result = new List<WikiEditSlotDto>();
        if (slots is null)
            return result;

        result.Add(new WikiEditSlotDto
        {
            Name = "main",
            ContentModel = slots.Main.RevSlotContentModel,
            SizeBytes = slots.Main.RevSlotSize
        });

        foreach (var name in slots.AdditionalProperties.Keys)
        {
            if (slots.AdditionalProperties.TryGetValue(name, out var slot))
            {
                result.Add(new WikiEditSlotDto { Name = name, ContentModel = slot.RevSlotContentModel, SizeBytes = slot.RevSlotSize });
            }
            else if (slots.AdditionalProperties.TryGetElement(name, out var raw))
            {
                // A slot whose shape drifted from the SDK model is still reported, with what can be read.
                result.Add(SlotFromJson(name, raw));
            }
        }

        return result;
    }

    private static bool TryReadTolerant(string frame, out WikiRevision? revision)
    {
        revision = null;
        try
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var title = GetString(root, "page_title");
            if (string.IsNullOrEmpty(title))
                return false;
            if (!root.TryGetProperty("rev_id", out var revId) || revId.ValueKind != JsonValueKind.Number || !revId.TryGetInt64(out var revisionId))
                return false;

            string? domain = null, uri = null;
            if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                domain = GetString(meta, "domain");
                uri = GetString(meta, "uri");
            }

            string? editor = null;
            if (root.TryGetProperty("performer", out var performer) && performer.ValueKind == JsonValueKind.Object)
                editor = GetString(performer, "user_text");

            var timestamp = ParseTimestamp(GetString(root, "rev_timestamp")) ?? ParseTimestamp(GetString(root, "dt")) ?? default;

            var slots = new List<WikiEditSlotDto>();
            if (root.TryGetProperty("rev_slots", out var revSlots) && revSlots.ValueKind == JsonValueKind.Object)
            {
                foreach (var slot in revSlots.EnumerateObject())
                {
                    var dto = SlotFromJson(slot.Name, slot.Value);
                    if (slot.NameEquals("main"))
                        slots.Insert(0, dto);
                    else
                        slots.Add(dto);
                }
            }

            revision = new WikiRevision(WikiOf(domain, uri), title, revisionId, editor, timestamp, slots);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static WikiEditSlotDto SlotFromJson(string name, JsonElement raw)
    {
        var dto = new WikiEditSlotDto { Name = name };
        if (raw.ValueKind != JsonValueKind.Object)
            return dto;

        dto.ContentModel = GetString(raw, "rev_slot_content_model");
        if (raw.TryGetProperty("rev_slot_size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var bytes))
            dto.SizeBytes = bytes;

        return dto;
    }

    private static string? WikiOf(string? domain, string? uri)
    {
        if (!string.IsNullOrWhiteSpace(domain))
            return domain;

        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Host : null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ParseTimestamp(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}
