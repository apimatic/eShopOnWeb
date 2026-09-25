using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The phone type.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PhoneType>))]
public sealed record PhoneType : OpenStringEnum<PhoneType>
{
    private PhoneType(string value) : base(value)
    {
    }

    /// <summary>
    /// Fax number.
    /// </summary>
    public static readonly PhoneType Fax = new("FAX");

    /// <summary>
    /// Home phone number.
    /// </summary>
    public static readonly PhoneType Home = new("HOME");

    /// <summary>
    /// Mobile phone number.
    /// </summary>
    public static readonly PhoneType Mobile = new("MOBILE");

    /// <summary>
    /// Other phone number.
    /// </summary>
    public static readonly PhoneType Other = new("OTHER");

    /// <summary>
    /// Pager number.
    /// </summary>
    public static readonly PhoneType Pager = new("PAGER");

    public TResult Match<TResult>(Func<TResult> onFax,
        Func<TResult> onHome,
        Func<TResult> onMobile,
        Func<TResult> onOther,
        Func<TResult> onPager,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Fax => onFax(),
            _ when this == Home => onHome(),
            _ when this == Mobile => onMobile(),
            _ when this == Other => onOther(),
            _ when this == Pager => onPager(),
            _ => otherwise(Value)
        };

    public void Match(Action onFax,
        Action onHome,
        Action onMobile,
        Action onOther,
        Action onPager,
        Action<string> otherwise)
    {
        if (this == Fax) onFax();
        else if (this == Home) onHome();
        else if (this == Mobile) onMobile();
        else if (this == Other) onOther();
        else if (this == Pager) onPager();
        else otherwise(Value);
    }
}
