using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// Model preset used for the agent run
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Model1>))]
public sealed record Model1 : StringEnum<Model1>
{
    private Model1(string value) : base(value)
    {
    }

    public static readonly Model1 Spark1Pro = new("spark-1-pro");

    public static readonly Model1 Spark1Mini = new("spark-1-mini");

    public static Model1 FromValue(string value) => FromValueCore(value);
}
