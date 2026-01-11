using System.Globalization;


namespace MemoUploader.Events;

public static class LogParser
{
    public static uint TryParseHex(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }
}
