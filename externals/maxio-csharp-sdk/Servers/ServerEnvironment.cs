using System;
using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Servers;

[JsonConverter(typeof(StringEnumConverter<ServerEnvironment>))]
public record ServerEnvironment : StringEnum<ServerEnvironment>
{
    /// <summary>
    /// Default Advanced Billing environment hosted in US. Valid for the majority of our customers.
    /// </summary>
    public static readonly ServerEnvironment Us = new("US");
    /// <summary>
    /// Advanced Billing environment hosted in EU. Use only when you requested EU hosting for your AB account.
    /// </summary>
    public static readonly ServerEnvironment Eu = new("EU");
    /// <summary>
    /// Access Advanced Billing through a Maxio API Gateway connector. Authenticate with your connector Bearer token instead of Basic auth. Events-Based Billing ingestion does not go through the gateway and keeps its direct URL.
    /// </summary>
    public static readonly ServerEnvironment MaxioApiGateway = new("Maxio API Gateway");

    private ServerEnvironment(string value) : base(value)
    {
    }

    internal T Match<T>(Func<T> onUs, Func<T> onEu, Func<T> onMaxioApiGateway) =>
        this switch
        {
            _ when this == Us => onUs(),
            _ when this == Eu => onEu(),
            _ when this == MaxioApiGateway => onMaxioApiGateway(),
            _ => throw new ArgumentOutOfRangeException(nameof(ServerEnvironment),
                this,
                $"Unknown {nameof(ServerEnvironment)} value.")
        };

    public static ServerEnvironment Default() => Us;
}
