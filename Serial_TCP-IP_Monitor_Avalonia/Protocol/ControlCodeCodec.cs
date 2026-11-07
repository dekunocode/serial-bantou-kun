using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Serial_TCP_IP_Monitor_Avalonia.Protocol;

public static class ControlCodeCodec
{
    private static readonly Regex TokenPattern = new(@"\[([A-Za-z0-9]+)\]", RegexOptions.Compiled);

    private static readonly Dictionary<string, byte> ControlCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "NUL", 0x00 }, { "SOH", 0x01 }, { "STX", 0x02 }, { "ETX", 0x03 },
        { "EOT", 0x04 }, { "ENQ", 0x05 }, { "ACK", 0x06 }, { "BEL", 0x07 },
        { "BS", 0x08 }, { "TAB", 0x09 }, { "LF", 0x0A }, { "VT", 0x0B },
        { "FF", 0x0C }, { "CR", 0x0D }, { "SO", 0x0E }, { "SI", 0x0F },
        { "DLE", 0x10 }, { "DC1", 0x11 }, { "DC2", 0x12 }, { "DC3", 0x13 },
        { "DC4", 0x14 }, { "NAK", 0x15 }, { "SYN", 0x16 }, { "ETB", 0x17 },
        { "CAN", 0x18 }, { "EM", 0x19 }, { "SUB", 0x1A }, { "ESC", 0x1B },
        { "FS", 0x1C }, { "GS", 0x1D }, { "RS", 0x1E }, { "US", 0x1F },
        { "DEL", 0x7F }
    };

    public static byte[] ParseMixedText(string? input, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        if (string.IsNullOrEmpty(input))
        {
            return [];
        }

        var output = new List<byte>();
        var currentIndex = 0;
        foreach (Match match in TokenPattern.Matches(input))
        {
            if (!TryParseToken(match.Groups[1].Value, out var tokenByte))
            {
                continue;
            }

            AddEncodedText(output, input[currentIndex..match.Index], encoding);
            output.Add(tokenByte);
            currentIndex = match.Index + match.Length;
        }

        AddEncodedText(output, input[currentIndex..], encoding);
        return [.. output];
    }

    public static bool TryParseHexBytes(string? input, out byte[] bytes, out string errorMessage)
    {
        var output = new List<byte>();
        var normalized = (input ?? string.Empty).Replace(",", " ").Replace("-", " ");
        var tokens = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawToken in tokens)
        {
            var token = rawToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? rawToken[2..]
                : rawToken;

            if (token.Length != 2 || !byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                bytes = [];
                errorMessage = $"'{rawToken}' は2桁の16進数ではありません。";
                return false;
            }

            output.Add(value);
        }

        bytes = [.. output];
        errorMessage = string.Empty;
        return true;
    }

    public static string ToHex(byte[]? data) => data is not { Length: > 0 }
        ? string.Empty
        : BitConverter.ToString(data).Replace('-', ' ');

    private static bool TryParseToken(string token, out byte value)
    {
        if (ControlCodes.TryGetValue(token, out value))
        {
            return true;
        }

        return token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && token.Length == 4
            && byte.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static void AddEncodedText(List<byte> output, string text, Encoding encoding)
    {
        if (!string.IsNullOrEmpty(text))
        {
            output.AddRange(encoding.GetBytes(text));
        }
    }
}
