using System.Globalization;
using System.Text.RegularExpressions;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed partial class SpeechTemplateProvider
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<ReminderPresentationKind, int> _lastTemplateIndexes = [];

    public string? Resolve(ReminderPresentationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SpeechText))
        {
            return null;
        }

        var templates = CreateTemplates(request);
        return templates.Count == 0
            ? request.SpeechText
            : PickWithoutImmediateRepeat(request.Kind, templates);
    }

    private static IReadOnlyList<string> CreateTemplates(ReminderPresentationRequest request)
    {
        var source = $"{request.Message} {request.SpeechText}";
        var isOvertime = source.Contains("加班", StringComparison.Ordinal);
        return request.Kind switch
        {
            ReminderPresentationKind.Drink when isOvertime =>
            [
                "还在加班呀。记得也照顾好自己，喝口水吧。",
                "辛苦啦。加班也别忘了喝水和活动一下。",
                "加班也要记得补充水分。喝口水，再继续吧。"
            ],
            ReminderPresentationKind.Drink =>
            [
                "工作一会儿啦，喝口水吧。",
                "记得喝点水，顺便放松一下眼睛。",
                "喝口水吧。休息一下，再继续。",
                "补充一点水分，再继续工作吧。"
            ],
            ReminderPresentationKind.Stand when isOvertime =>
            [
                "加班也别一直坐着。起来走一走吧。",
                "已经加班一阵子啦。伸个懒腰，活动两分钟吧。",
                "辛苦啦。先起来活动一下，再继续。"
            ],
            ReminderPresentationKind.Stand => CreateStandTemplates(source),
            ReminderPresentationKind.LunchSoon => CreateLunchSoonTemplates(source),
            ReminderPresentationKind.LunchStart =>
            [
                "上午辛苦啦。午休时间到了，好好休息一下吧。",
                "到午休时间啦。先把工作放一放，休息会儿吧。",
                "午休开始啦。放松一下，下午再继续。"
            ],
            ReminderPresentationKind.AfternoonStart =>
            [
                "下午好。休息结束啦，慢慢进入状态吧。",
                "下午的工作开始啦。别着急，慢慢来。",
                "午休结束啦。活动一下，准备开始下午的工作吧。"
            ],
            ReminderPresentationKind.OffWork =>
            [
                "今天的正常工作时间结束啦。辛苦了。",
                "下班时间到啦。今天也辛苦了，好好放松一下吧。",
                "今天的工作先到这里吧。记得收好尾，好好休息。"
            ],
            ReminderPresentationKind.Cleaning => CreateCleaningTemplates(source),
            ReminderPresentationKind.OvertimeSuggestion =>
            [
                "还在继续工作吗？如果需要，可以进入加班模式。",
                "下班后还在忙呀。需要开始记录加班时间吗？",
                "检测到你还在工作。要进入加班模式吗？"
            ],
            _ => []
        };
    }

    private static IReadOnlyList<string> CreateStandTemplates(string source)
    {
        var minutes = ExtractFirstNumber(source);
        var duration = minutes is null
            ? "已经连续工作一阵子了"
            : $"已经连续工作{SpeechTextNormalizer.ToChineseNumber(minutes.Value)}分钟了";
        return
        [
            $"坐得有点久啦。{duration}，起来活动一下吧。",
            "起来走一走吧，也让眼睛休息一会儿。",
            "工作挺久了。伸个懒腰，活动两分钟吧。",
            "先活动一下身体，再回来继续吧。"
        ];
    }

    private static IReadOnlyList<string> CreateLunchSoonTemplates(string source)
    {
        var minutes = ExtractFirstNumber(source) ?? 5;
        var spokenMinutes = SpeechTextNormalizer.ToChineseNumber(minutes);
        return
        [
            $"还有{spokenMinutes}分钟午休。手头的事情，可以慢慢收个尾啦。",
            "快到午休时间啦。记得保存一下现在的工作。",
            $"再过{spokenMinutes}分钟就午休啦。可以准备收尾了。"
        ];
    }

    private static IReadOnlyList<string> CreateCleaningTemplates(string source)
    {
        var minutes = ExtractFirstNumber(source);
        if (minutes == 30 || source.Contains("半小时", StringComparison.Ordinal))
        {
            return
            [
                "快下班啦。记得把工位和周围整理一下。",
                "还有半小时下班，可以顺手收拾一下工位啦。",
                "今天也快结束了。把桌面和周围简单整理一下吧。"
            ];
        }

        var duration = minutes is null
            ? "还有一会儿"
            : $"还有{SpeechTextNormalizer.ToChineseNumber(minutes.Value)}分钟";
        return
        [
            $"{duration}下班。可以顺手收拾一下工位啦。",
            "快下班啦。记得把工位和周围整理一下。",
            "今天也快结束了。把桌面简单整理一下吧。"
        ];
    }

    private string PickWithoutImmediateRepeat(
        ReminderPresentationKind kind,
        IReadOnlyList<string> templates)
    {
        lock (_syncRoot)
        {
            var index = templates.Count == 1 ? 0 : Random.Shared.Next(templates.Count);
            if (templates.Count > 1 &&
                _lastTemplateIndexes.TryGetValue(kind, out var lastIndex) &&
                index == lastIndex)
            {
                index = (index + 1 + Random.Shared.Next(templates.Count - 1)) % templates.Count;
            }

            _lastTemplateIndexes[kind] = index;
            return templates[index];
        }
    }

    private static int? ExtractFirstNumber(string text)
    {
        var match = NumberRegex().Match(text);
        return match.Success && int.TryParse(
            match.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();
}
