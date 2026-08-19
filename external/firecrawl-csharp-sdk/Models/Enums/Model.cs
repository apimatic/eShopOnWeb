using System.Text.Json.Serialization;
using FirecrawlApi.Core.Enum;

namespace FirecrawlApi.Models.Enums;

/// <summary>
/// The model to use for the agent task. spark-1-mini (default) is 60% cheaper, spark-1-pro offers higher accuracy for complex tasks
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Model>))]
public sealed record Model : StringEnum<Model>
{
    private Model(string value) : base(value)
    {
    }

    public static readonly Model Spark1Mini = new("spark-1-mini");

    public static readonly Model Spark1Pro = new("spark-1-pro");

    public static Model FromValue(string value) => FromValueCore(value);
}
