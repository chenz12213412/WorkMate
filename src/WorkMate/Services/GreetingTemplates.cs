namespace WorkMate.Services;

internal static class GreetingTemplates
{
    public static readonly string[] BeforeWorkGreeting =
    [
        "早上好，现在还没到上班时间，WorkMate 已经准备好了。",
        "早上好，离今天上班还有一点时间，先从容准备一下吧。"
    ];

    public static readonly string[] WorkingGreeting =
    [
        "你好，现在是工作时间，我会安静记录并适时提醒你休息。",
        "工作已经开始了，记得喝水，坐久了我会提醒你活动。"
    ];

    public static readonly string[] LunchGreeting =
    [
        "现在是午休时间，工作计时和普通提醒已经暂停，好好休息吧。",
        "午休时间到了，WorkMate 会暂停工作提醒，下午再继续。"
    ];

    public static readonly string[] AfterWorkGreeting =
    [
        "今天已经下班了，普通工作提醒和工作计时都已暂停。",
        "现在是下班时间，今天辛苦了，记得好好休息。"
    ];

    public static readonly string[] WeekendGreeting =
    [
        "周末好，今天也要工作吗？记得给自己留点休息时间。",
        "周末好，WorkMate 已准备好，需要时我会提醒你休息。"
    ];
}
