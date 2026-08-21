using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

public static class PhoneNumberSanitizer
{
    public static string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '+' && i + 1 < value.Length && char.IsDigit(value[i + 1]))
            {
                result.Append("+[redacted]");
                i++;
                while (i < value.Length && (char.IsDigit(value[i]) || value[i] == ' ' || value[i] == '-'))
                {
                    i++;
                }

                i--;
                continue;
            }

            result.Append(value[i]);
        }

        return result.ToString();
    }
}
