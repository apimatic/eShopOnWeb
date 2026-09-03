using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CustomerCareChannel>))]
public sealed record CustomerCareChannel : StringEnum<CustomerCareChannel>
{
    private CustomerCareChannel(string value) : base(value)
    {
    }

    public static readonly CustomerCareChannel TollFreeNumber = new("TOLL_FREE_NUMBER");

    public static readonly CustomerCareChannel Email = new("EMAIL");

    public static CustomerCareChannel FromValue(string value) => FromValueCore(value);
}
