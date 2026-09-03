using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Servers;

[JsonConverter(typeof(StringEnumConverter<ServerEnvironment>))]
public record ServerEnvironment : StringEnum<ServerEnvironment>
{
    /// <summary>
    /// PayPal Sandbox Environment
    /// </summary>
    public static readonly ServerEnvironment Sandbox = new("Sandbox");

    private ServerEnvironment(string value) : base(value)
    {
    }

    internal T Match<T>(Func<T> onSandbox) =>
        this switch
        {
            _ when this == Sandbox => onSandbox(),
            _ => throw new ArgumentOutOfRangeException(nameof(ServerEnvironment),
                this,
                $"Unknown {nameof(ServerEnvironment)} value.")
        };

    public static ServerEnvironment Default() => Sandbox;
}
