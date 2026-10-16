namespace TerminalSimulation.Wpf.Services;

public sealed record TrafficEventRule
{
    public bool Enabled { get; init; } = true;
    public double ProbabilityPercent { get; init; } = 60;
    public double IntervalSeconds { get; init; } = 180;
    public double MinDurationSeconds { get; init; } = 8;
    public double MaxDurationSeconds { get; init; } = 25;
    public int MinCycles { get; init; } = 1;
    public int MaxCycles { get; init; } = 1;
    public double SlowSpeedKph { get; init; } = 12;
}

public sealed record TrafficEventDefinition(string Id, string Name, string Group, string Description,
    bool HasCycles, bool HasSlowSpeed, TrafficEventRule Defaults);

// This catalog is also sent to both independent Web UIs. IDs are persisted in .ctsroute files.
public static class TrafficEventCatalog
{
    public static IReadOnlyList<TrafficEventDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        Define("signalSingle", "路口红灯 · 一轮放行", "路口通行", "提前减速停车，等待红灯，绿灯后带起步反应时间平顺通行。", 150, 70, 15, 55),
        Define("signalQueue", "路口红灯 · 多轮排队", "路口通行", "红灯等待、绿灯队列前移、再次停车，经过多轮信号周期后通过。", 300, 45, 20, 65, 2, 4, 9),
        Define("leftTurnYield", "无保护左转让行", "路口通行", "仅在左转前，停车等待对向车流间隙，再缓慢穿过路口。", 180, 55, 4, 18, slow: 12),
        Define("crosswalkYield", "人行横道礼让", "路口通行", "在虚拟过街点前停车，等待行人通过，再低速起步。", 240, 50, 5, 20, slow: 10),
        Define("stopGo", "拥堵车流 · 走走停停", "车流变化", "跟随队列蠕行、停车、反应起步，反复推进后逐步恢复。", 300, 55, 4, 14, 3, 6, 10),
        Define("phantomJam", "幽灵堵车 · 连续减速波", "车流变化", "没有固定障碍的车流压缩波，减速、短暂恢复、再次减速，最终消散。", 360, 45, 8, 22, 2, 4, 14),
        Define("slowLeader", "慢车跟行", "车流变化", "在一段道路上受慢车影响，目标速度连续小幅变化，随后恢复巡航。", 240, 60, 20, 65, slow: 24),
        Define("cutIn", "邻车切入 · 拉开车距", "车流变化", "邻车切入后先收油减速，保持安全间隔，再平顺恢复；不改变轨迹。", 180, 45, 3, 8, slow: 20),
        Define("mergeAlternating", "汇入口交替通行", "车流变化", "接近合流队尾减速，停车让行、低速前移，再跟随车流汇入。", 300, 45, 3, 10, 1, 3, 12),
        Define("busObstruction", "公交停靠 / 临时占道", "道路干扰", "前方车道临时受阻，停车等待障碍解除，再缓慢通过。", 360, 35, 8, 30, slow: 12),
        Define("roadworks", "施工窄道 · 交替放行", "道路干扰", "在施工路段入口停车等候，获准后低速通过受限路段，再恢复。", 600, 35, 12, 45, slow: 15),
        Define("emergencyYield", "应急车辆避让", "道路干扰", "平顺减速至停车，让应急车辆通过，稍作观察后继续行驶。", 900, 20, 5, 18, slow: 10),
        Define("speedBump", "减速带 / 破损路面", "道路干扰", "提前收油减速，低速通过短距离颠簸路段，离开后恢复速度。", 300, 45, 3, 7, slow: 10),
        Define("railCrossing", "铁路道口等待", "道路干扰", "在虚拟道口前停车，等待栏杆开启后低速通过；默认关闭。", 1200, 20, 35, 100, slow: 12, enabled: false)
    });

    private static TrafficEventDefinition Define(string id, string name, string group, string description,
        double interval, double probability, double min, double max, int cyclesMin = 1, int cyclesMax = 1,
        double? slow = null, bool enabled = true) => new(id, name, group, description, cyclesMax > 1, slow.HasValue,
        new() { Enabled = enabled, IntervalSeconds = interval, ProbabilityPercent = probability,
            MinDurationSeconds = min, MaxDurationSeconds = max, MinCycles = cyclesMin, MaxCycles = cyclesMax,
            SlowSpeedKph = slow ?? 12 });

    public static Dictionary<string, TrafficEventRule> CreateDefaults() => All.ToDictionary(x => x.Id, x => x.Defaults with { });
}

public sealed record TrafficEventOptions
{
    public double MinimumGapSeconds { get; init; } = 30;
    public bool AllowVirtualSignalsOnStraight { get; init; } = true;
    public double VirtualSignalSpacingMeters { get; init; } = 450;
    public Dictionary<string, TrafficEventRule> Events { get; init; } = TrafficEventCatalog.CreateDefaults();

    public TrafficEventOptions Validate()
    {
        static void Range(double value, double min, double max, string label)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"{label}必须为 {min}–{max}");
        }
        Range(MinimumGapSeconds, 5, 600, "事件最小间隔（秒）");
        Range(VirtualSignalSpacingMeters, 100, 5000, "虚拟路口间距（米）");
        if (Events is null) throw new ArgumentException("交通事件设置不能为空");
        var result = TrafficEventCatalog.CreateDefaults(); // Old files may omit newly added event types.
        foreach (var (id, rule) in Events)
        {
            if (!result.ContainsKey(id) || rule is null) throw new ArgumentException($"交通事件设置无效：{id}");
            Range(rule.ProbabilityPercent, 0, 100, "触发概率（%）");
            Range(rule.IntervalSeconds, 5, 3600, "机会间隔（秒）");
            Range(rule.MinDurationSeconds, 1, 180, "最短阶段时间（秒）");
            Range(rule.MaxDurationSeconds, rule.MinDurationSeconds, 180, "最长阶段时间（秒）");
            Range(rule.MinCycles, 1, 6, "最少轮数");
            Range(rule.MaxCycles, rule.MinCycles, 6, "最多轮数");
            Range(rule.SlowSpeedKph, 2, 60, "缓行速度（km/h）");
            result[id] = rule with { };
        }
        return this with { Events = result };
    }
}
