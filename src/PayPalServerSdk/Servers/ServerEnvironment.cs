using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Servers;

[JsonConverter(typeof(StringEnumConverter<ServerEnvironment>))]
public sealed record ServerEnvironment : ClosedStringEnum<ServerEnvironment>
{
    private ServerEnvironment(string value) : base(value)
    {
    }

    /// <summary>
    /// PayPal Sandbox Environment
    /// </summary>
    public static readonly ServerEnvironment Sandbox = new("Sandbox");

    public static ServerEnvironment Default() => Sandbox;

    internal TResult Match<TResult>(Func<TResult> onSandbox) =>
        this switch
        {
            _ when this == Sandbox => onSandbox(),
            _ => throw new InvalidOperationException($"{nameof(ServerEnvironment)} holds no known value.")
        };
}
