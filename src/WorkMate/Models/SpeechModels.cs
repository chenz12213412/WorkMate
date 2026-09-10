namespace WorkMate.Models;

public sealed record SpeechVoiceInfo(
    string Name,
    string Culture,
    string Gender,
    string Description)
{
    public string DisplayName => $"{ToCultureLabel(Culture)} - {Name}";

    private static string ToCultureLabel(string culture)
    {
        if (culture.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return "中文（简体）";
        }

        if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "中文";
        }

        return culture;
    }
}
