using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Operating System type of the device that the buyer is using.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OsType>))]
public sealed record OsType : OpenStringEnum<OsType>
{
    private OsType(string value) : base(value)
    {
    }

    /// <summary>
    /// Google Android OS.
    /// </summary>
    public static readonly OsType Android = new("ANDROID");

    /// <summary>
    /// Apple OS typically found in Apple mobile devices.
    /// </summary>
    public static readonly OsType Ios = new("IOS");

    /// <summary>
    /// Any other OS type.
    /// </summary>
    public static readonly OsType Other = new("OTHER");

    public TResult Match<TResult>(Func<TResult> onAndroid,
        Func<TResult> onIos,
        Func<TResult> onOther,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Android => onAndroid(),
            _ when this == Ios => onIos(),
            _ when this == Other => onOther(),
            _ => otherwise(Value)
        };

    public void Match(Action onAndroid, Action onIos, Action onOther, Action<string> otherwise)
    {
        if (this == Android) onAndroid();
        else if (this == Ios) onIos();
        else if (this == Other) onOther();
        else otherwise(Value);
    }
}
