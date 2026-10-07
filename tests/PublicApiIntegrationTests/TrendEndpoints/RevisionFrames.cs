using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PublicApiIntegrationTests.TrendEndpoints;

/// <summary>
/// Builds mediawiki.revision-create frames using the wire names declared by the SDK's MediawikiRevisionCreate model.
/// </summary>
public static class RevisionFrames
{
    public static string Revision(
        string domain,
        string title,
        long revId,
        string user = "Editor",
        IDictionary<string, (string Model, long Size)>? extraSlots = null)
    {
        var slots = new Dictionary<string, object>
        {
            ["main"] = Slot("wikitext", 1234)
        };
        foreach (var (name, slot) in extraSlots ?? new Dictionary<string, (string, long)>())
            slots[name] = Slot(slot.Model, slot.Size);

        var frame = new Dictionary<string, object?>
        {
            ["$schema"] = "/mediawiki/revision/create/2.0.0",
            ["database"] = domain.Split('.')[0] + "wiki",
            ["dt"] = "2026-10-08T10:00:00Z",
            ["meta"] = new Dictionary<string, object?>
            {
                ["domain"] = domain,
                ["stream"] = "mediawiki.revision-create",
                ["uri"] = $"https://{domain}/wiki/{title}"
            },
            ["page_id"] = 42,
            ["page_is_redirect"] = false,
            ["page_namespace"] = 0,
            ["page_title"] = title,
            ["performer"] = new Dictionary<string, object?> { ["user_text"] = user },
            ["rev_id"] = revId,
            ["rev_slots"] = slots,
            ["rev_timestamp"] = "2026-10-08T10:00:00Z"
        };

        return JsonSerializer.Serialize(frame);
    }

    private static Dictionary<string, object> Slot(string model, long size) => new()
    {
        ["rev_slot_content_model"] = model,
        ["rev_slot_sha1"] = "abc",
        ["rev_slot_size"] = size,
        ["rev_slot_origin_rev_id"] = 1
    };

    /// <summary>
    /// Serializes frames as a text/event-stream body.
    /// </summary>
    public static string Body(params string[] frames) =>
        string.Concat(frames.Select(f => $"event: message\nid: [{{\"topic\":\"t\",\"partition\":0}}]\ndata: {f}\n\n"));
}
