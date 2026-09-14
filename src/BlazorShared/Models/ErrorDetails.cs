using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorShared.Models;

public class ErrorDetails
{
    public int StatusCode { get; set; }
    public string Message { get; set; }

    /// <summary>
    /// Detail messages behind <see cref="Message"/>, when the failure came from a downstream system
    /// that reported more than one problem. Omitted when there are none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string> Errors { get; set; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}
