using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsCourseReminder;

internal sealed class ScheduleEvent
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("weekday")]
    public string? Weekday { get; set; }

    [JsonPropertyName("dayOfWeek")]
    public int? DayOfWeek { get; set; }

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; }
}

internal sealed class ScheduleFile
{
    [JsonPropertyName("calendarEvents")]
    public List<ScheduleEvent> CalendarEvents { get; set; } = [];
}

internal sealed record ScheduledEvent(string Title, DateTime Start, DateTime? End);

internal static class ScheduleReader
{
    private static readonly Dictionary<string, DayOfWeek> WeekdayAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["星期日"] = DayOfWeek.Sunday, ["星期天"] = DayOfWeek.Sunday, ["周日"] = DayOfWeek.Sunday,
        ["周天"] = DayOfWeek.Sunday, ["日"] = DayOfWeek.Sunday, ["天"] = DayOfWeek.Sunday,
        ["sunday"] = DayOfWeek.Sunday, ["sun"] = DayOfWeek.Sunday,
        ["星期一"] = DayOfWeek.Monday, ["周一"] = DayOfWeek.Monday, ["一"] = DayOfWeek.Monday,
        ["monday"] = DayOfWeek.Monday, ["mon"] = DayOfWeek.Monday,
        ["星期二"] = DayOfWeek.Tuesday, ["周二"] = DayOfWeek.Tuesday, ["二"] = DayOfWeek.Tuesday,
        ["tuesday"] = DayOfWeek.Tuesday, ["tue"] = DayOfWeek.Tuesday,
        ["星期三"] = DayOfWeek.Wednesday, ["周三"] = DayOfWeek.Wednesday, ["三"] = DayOfWeek.Wednesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["wed"] = DayOfWeek.Wednesday,
        ["星期四"] = DayOfWeek.Thursday, ["周四"] = DayOfWeek.Thursday, ["四"] = DayOfWeek.Thursday,
        ["thursday"] = DayOfWeek.Thursday, ["thu"] = DayOfWeek.Thursday,
        ["星期五"] = DayOfWeek.Friday, ["周五"] = DayOfWeek.Friday, ["五"] = DayOfWeek.Friday,
        ["friday"] = DayOfWeek.Friday, ["fri"] = DayOfWeek.Friday,
        ["星期六"] = DayOfWeek.Saturday, ["周六"] = DayOfWeek.Saturday, ["六"] = DayOfWeek.Saturday,
        ["saturday"] = DayOfWeek.Saturday, ["sat"] = DayOfWeek.Saturday
    };

    public static ScheduleFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ScheduleFile>(json)
            ?? throw new InvalidDataException("课程表.json 内容为空或格式无效。");
    }

    public static ScheduledEvent? FindNext(IEnumerable<ScheduleEvent> events, DateTime now)
    {
        var today = now.Date;
        var candidates = new List<ScheduledEvent>();
        foreach (var item in events)
        {
            if (!TryGetWeekday(item, out var weekday) || !TimeOnly.TryParse(item.StartTime, out var startTime))
            {
                continue;
            }

            var offset = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
            var start = today.AddDays(offset).Add(startTime.ToTimeSpan());
            if (start <= now)
            {
                start = start.AddDays(7);
            }

            DateTime? end = null;
            if (item.EndTime is not null && TimeOnly.TryParse(item.EndTime, out var endTime))
            {
                end = start.Date.Add(endTime.ToTimeSpan());
                if (end <= start)
                {
                    end = end.Value.AddDays(1);
                }
            }
            candidates.Add(new ScheduledEvent(item.Title.Trim(), start, end));
        }

        return candidates.OrderBy(item => item.Start).FirstOrDefault();
    }

    private static bool TryGetWeekday(ScheduleEvent item, out DayOfWeek weekday)
    {
        if (item.DayOfWeek is int numeric)
        {
            if (numeric is >= 0 and <= 6)
            {
                weekday = (DayOfWeek)numeric;
                return true;
            }
            if (numeric is >= 1 and <= 7)
            {
                weekday = (DayOfWeek)(numeric % 7);
                return true;
            }
        }

        return WeekdayAliases.TryGetValue(item.Weekday?.Trim() ?? string.Empty, out weekday);
    }
}

internal sealed class MarqueeForm : Form
{
    private readonly Label textLabel;
    private readonly System.Windows.Forms.Timer animationTimer;
    private readonly Action finished;
    private int completedLoops;
    private bool finishing;

    public MarqueeForm(string text, Action finished)
    {
        this.finished = finished;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        ForeColor = Color.White;
        Width = 760;
        Height = 86;

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1440, 900);
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + 50);

        textLabel = new Label
        {
            AutoSize = true,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 24, FontStyle.Bold),
            Text = text,
            Top = 20,
            Left = Width
        };
        Controls.Add(textLabel);

        animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animationTimer.Tick += Animate;
        Shown += (_, _) => animationTimer.Start();
        FormClosed += (_, _) => animationTimer.Dispose();
    }

    private void Animate(object? sender, EventArgs e)
    {
        textLabel.Left -= 2;
        if (textLabel.Right >= 0)
        {
            return;
        }

        completedLoops++;
        if (completedLoops >= 3)
        {
            animationTimer.Stop();
            if (finishing)
            {
                return;
            }
            finishing = true;
            // Close after this timer callback returns, so the form is not disposed mid-tick.
            BeginInvoke(finished);
            return;
        }
        textLabel.Left = Width;
    }
}

internal sealed class ReminderApplicationContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem;
    private readonly System.Windows.Forms.Timer scheduleTimer;
    private readonly string configPath;
    private List<ScheduleEvent> events = [];
    private ScheduledEvent? nextEvent;
    private readonly HashSet<int> announcedMinutes = [];
    private MarqueeForm? activeMarquee;

    public ReminderApplicationContext()
    {
        configPath = Path.Combine(AppContext.BaseDirectory, "课程表.json");
        statusItem = new ToolStripMenuItem("正在读取课程表…") { Enabled = false };
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "课程提醒",
            Visible = true,
            ContextMenuStrip = menu
        };

        scheduleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        scheduleTimer.Tick += (_, _) => CheckSchedule();
        LoadSchedule();
    }

    private void LoadSchedule()
    {
        try
        {
            var data = ScheduleReader.Load(configPath);
            events = data.CalendarEvents;
            if (events.Count == 0)
            {
                UpdateStatus("课程表为空");
                return;
            }

            UpdateNextEvent();
            if (nextEvent is not null && nextEvent.Start > DateTime.Now.AddMinutes(5))
            {
                ShowMarquee($"下一节预告：{nextEvent.Start:HH:mm} 上课：{nextEvent.Title}");
            }
            scheduleTimer.Start();
        }
        catch (Exception error)
        {
            UpdateStatus("读取失败");
            MessageBox.Show($"无法读取课程表.json：\n{error.Message}", "课程提醒启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CheckSchedule()
    {
        if (nextEvent is null)
        {
            UpdateNextEvent();
            return;
        }

        var remaining = nextEvent.Start - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            UpdateNextEvent();
            return;
        }

        var minutesBefore = (int)Math.Ceiling(remaining.TotalMinutes);
        if (minutesBefore is >= 1 and <= 5 && announcedMinutes.Add(minutesBefore))
        {
            ShowMarquee($"{minutesBefore}分钟后上课：{nextEvent.Title}");
        }
        UpdateStatus($"下节：{nextEvent.Title}（{minutesBefore}分钟后）");
    }

    private void UpdateNextEvent()
    {
        nextEvent = ScheduleReader.FindNext(events, DateTime.Now);
        announcedMinutes.Clear();
        UpdateStatus(nextEvent is null
            ? "今天没有课程"
            : $"下节：{nextEvent.Title}（{nextEvent.Start:HH:mm}）");
    }

    private void ShowMarquee(string text)
    {
        activeMarquee?.Close();
        activeMarquee = new MarqueeForm(text, FinishMarquee);
        activeMarquee.FormClosed += (_, _) => activeMarquee = null;
        activeMarquee.Show();
    }

    private void FinishMarquee()
    {
        if (activeMarquee is null)
        {
            return;
        }
        activeMarquee.Close();
        activeMarquee = null;
    }

    private void UpdateStatus(string text)
    {
        statusItem.Text = text;
    }

    protected override void ExitThreadCore()
    {
        scheduleTimer.Stop();
        scheduleTimer.Dispose();
        activeMarquee?.Close();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ReminderApplicationContext());
    }
}
