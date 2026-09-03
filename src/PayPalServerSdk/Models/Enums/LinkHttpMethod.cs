using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The HTTP method required to make the related call.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<LinkHttpMethod>))]
public sealed record LinkHttpMethod : StringEnum<LinkHttpMethod>
{
    private LinkHttpMethod(string value) : base(value)
    {
    }

    /// <summary>
    /// The HTTP GET method.
    /// </summary>
    public static readonly LinkHttpMethod Get = new("GET");

    /// <summary>
    /// The HTTP POST method.
    /// </summary>
    public static readonly LinkHttpMethod Post = new("POST");

    /// <summary>
    /// The HTTP PUT method.
    /// </summary>
    public static readonly LinkHttpMethod Put = new("PUT");

    /// <summary>
    /// The HTTP DELETE method.
    /// </summary>
    public static readonly LinkHttpMethod Delete = new("DELETE");

    /// <summary>
    /// The HTTP HEAD method.
    /// </summary>
    public static readonly LinkHttpMethod Head = new("HEAD");

    /// <summary>
    /// The HTTP CONNECT method.
    /// </summary>
    public static readonly LinkHttpMethod Connect = new("CONNECT");

    /// <summary>
    /// The HTTP OPTIONS method.
    /// </summary>
    public static readonly LinkHttpMethod Options = new("OPTIONS");

    /// <summary>
    /// The HTTP PATCH method.
    /// </summary>
    public static readonly LinkHttpMethod Patch = new("PATCH");

    public static LinkHttpMethod FromValue(string value) => FromValueCore(value);
}
