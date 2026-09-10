using System.Globalization;
using System.Text.RegularExpressions;

namespace WorkMate.Services;

public static partial class SpeechTextNormalizer
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = WhitespaceRegex().Replace(text.Trim(), " ");
        normalized = HourMinuteDurationRegex().Replace(normalized, static match =>
            $"{ToDurationNumber(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))}小时" +
            $"{ToDurationNumber(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))}分钟");
        normalized = MinuteDurationRegex().Replace(normalized, static match =>
            $"{ToDurationNumber(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))}分钟");
        normalized = ClockTimeRegex().Replace(normalized, static match =>
            ToSpokenTime(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
        normalized = SpaceAroundPunctuationRegex().Replace(normalized, "$1");
        normalized = RepeatedPunctuationRegex().Replace(normalized, "$1");
        if (!SentenceEndingRegex().IsMatch(normalized))
        {
            normalized += "。";
        }

        return normalized;
    }

    public static string ToChineseNumber(int number)
    {
        if (number == 0)
        {
            return "零";
        }

        if (number < 0)
        {
            return $"负{ToChineseNumber(Math.Abs(number))}";
        }

        if (number > 9999)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        string[] digits = ["零", "一", "二", "三", "四", "五", "六", "七", "八", "九"];
        string[] units = [string.Empty, "十", "百", "千"];
        var characters = new List<string>();
        var divisor = 1000;
        var pendingZero = false;
        for (var position = 3; position >= 0; position--)
        {
            var digit = number / divisor % 10;
            if (digit == 0)
            {
                if (characters.Count > 0 && number % divisor != 0)
                {
                    pendingZero = true;
                }
            }
            else
            {
                if (pendingZero)
                {
                    characters.Add("零");
                    pendingZero = false;
                }

                if (!(digit == 1 && position == 1 && characters.Count == 0))
                {
                    characters.Add(digits[digit]);
                }

                characters.Add(units[position]);
            }

            divisor /= 10;
        }

        return string.Concat(characters);
    }

    private static string ToDurationNumber(int number) => number == 2 ? "两" : ToChineseNumber(number);

    private static string ToSpokenTime(int hour, int minute)
    {
        var period = hour switch
        {
            < 6 => "凌晨",
            < 12 => "上午",
            12 => "中午",
            < 18 => "下午",
            _ => "晚上"
        };
        var spokenHour = hour > 12 ? hour - 12 : hour == 0 ? 12 : hour;
        var minuteText = minute switch
        {
            0 => string.Empty,
            30 => "半",
            _ => $"{ToChineseNumber(minute)}分"
        };
        return $"{period}{ToChineseNumber(spokenHour)}点{minuteText}";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<!\d)(\d+)\s*h\s*(\d+)\s*m\b", RegexOptions.IgnoreCase)]
    private static partial Regex HourMinuteDurationRegex();

    [GeneratedRegex(@"(?<!\d)(\d+)\s*(?:min|分钟)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MinuteDurationRegex();

    [GeneratedRegex(@"(?<!\d)([01]?\d|2[0-3]):([0-5]\d)(?!\d)")]
    private static partial Regex ClockTimeRegex();

    [GeneratedRegex(@"\s*([，。！？；：])\s*")]
    private static partial Regex SpaceAroundPunctuationRegex();

    [GeneratedRegex(@"([，。！？；：])\1+")]
    private static partial Regex RepeatedPunctuationRegex();

    [GeneratedRegex(@"[。！？]$")]
    private static partial Regex SentenceEndingRegex();
}
