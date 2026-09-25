using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The HTTP method required to make the related call.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<LinkHttpMethod>))]
public sealed record LinkHttpMethod : OpenStringEnum<LinkHttpMethod>
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

    public TResult Match<TResult>(Func<TResult> onGet,
        Func<TResult> onPost,
        Func<TResult> onPut,
        Func<TResult> onDelete,
        Func<TResult> onHead,
        Func<TResult> onConnect,
        Func<TResult> onOptions,
        Func<TResult> onPatch,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Get => onGet(),
            _ when this == Post => onPost(),
            _ when this == Put => onPut(),
            _ when this == Delete => onDelete(),
            _ when this == Head => onHead(),
            _ when this == Connect => onConnect(),
            _ when this == Options => onOptions(),
            _ when this == Patch => onPatch(),
            _ => otherwise(Value)
        };

    public void Match(Action onGet,
        Action onPost,
        Action onPut,
        Action onDelete,
        Action onHead,
        Action onConnect,
        Action onOptions,
        Action onPatch,
        Action<string> otherwise)
    {
        if (this == Get) onGet();
        else if (this == Post) onPost();
        else if (this == Put) onPut();
        else if (this == Delete) onDelete();
        else if (this == Head) onHead();
        else if (this == Connect) onConnect();
        else if (this == Options) onOptions();
        else if (this == Patch) onPatch();
        else otherwise(Value);
    }
}
