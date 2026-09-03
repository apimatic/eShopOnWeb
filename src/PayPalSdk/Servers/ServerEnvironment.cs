using System;
using System.Text.Json.Serialization;
using PayPal.Core.Enum;

namespace PayPal.Servers;

[JsonConverter(typeof(StringEnumConverter<ServerEnvironment>))]
public record ServerEnvironment : StringEnum<ServerEnvironment>
{
    /// <summary>
    /// PayPal Sandbox Environment
    /// </summary>
    public static readonly ServerEnvironment Production = new("production");

    private ServerEnvironment(string value) : base(value)
    {
    }

    internal T Match<T>(Func<T> onProduction) =>
        this switch
        {
            _ when this == Production => onProduction(),
            _ => throw new ArgumentOutOfRangeException(nameof(ServerEnvironment),
                this,
                $"Unknown {nameof(ServerEnvironment)} value.")
        };

    public static ServerEnvironment Default() => Production;
}
