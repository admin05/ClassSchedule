using System.Text.Json;
using System.Text.Json.Serialization;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

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
    private const string BundledFontResourceName = "WindowsCourseReminder.Resources.LXGWWenKaiMonoScreen.ttf";
    private const string BundledFontFamilyName = "LXGW WenKai Mono Screen";
    private readonly Label textLabel;
    private readonly System.Windows.Forms.Timer animationTimer;
    private readonly Action finished;
    private readonly PrivateFontCollection privateFonts = new();
    private readonly Font marqueeFont;
    private const double ScrollSpeed = 2.2;
    private double horizontalOffset;
    private byte[]? bundledFontBytes;
    private GCHandle bundledFontHandle;
    private int completedLoops;
    private bool finishing;

    public MarqueeForm(string text, Action finished)
    {
        this.finished = finished;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        ForeColor = Color.White;
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1440, 900);
        Width = screen.Width;
        Location = new Point(screen.Left, screen.Top + 50);

        marqueeFont = LoadBundledFont();
        textLabel = new Label
        {
            AutoSize = true,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = marqueeFont,
            Text = text,
            Left = Width,
            UseCompatibleTextRendering = true
        };
        horizontalOffset = textLabel.Left;
        Controls.Add(textLabel);

        // Size the banner from the rendered glyph height so high-DPI scaling cannot crop text.
        var textSize = TextRenderer.MeasureText(text, marqueeFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Height = Math.Max(86, textSize.Height + 24);
        textLabel.Top = Math.Max(0, (ClientSize.Height - textLabel.Height) / 2);
        DpiChanged += (_, _) => textLabel.Top = Math.Max(0, (ClientSize.Height - textLabel.Height) / 2);

        animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animationTimer.Tick += Animate;
        Shown += (_, _) => animationTimer.Start();
        FormClosed += (_, _) => animationTimer.Dispose();
    }

    private Font LoadBundledFont()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(BundledFontResourceName);
        if (stream is not null)
        {
            bundledFontBytes = new byte[stream.Length];
            stream.ReadExactly(bundledFontBytes);
            bundledFontHandle = GCHandle.Alloc(bundledFontBytes, GCHandleType.Pinned);
            privateFonts.AddMemoryFont(bundledFontHandle.AddrOfPinnedObject(), bundledFontBytes.Length);
            var family = privateFonts.Families.FirstOrDefault(item => item.Name.Equals(BundledFontFamilyName, StringComparison.OrdinalIgnoreCase))
                ?? privateFonts.Families.FirstOrDefault();
            if (family is not null)
            {
                return new Font(family, 30, FontStyle.Regular, GraphicsUnit.Point);
            }
        }

        return new Font("Microsoft YaHei UI", 30, FontStyle.Regular, GraphicsUnit.Point);
    }

    private void Animate(object? sender, EventArgs e)
    {
        horizontalOffset -= ScrollSpeed;
        textLabel.Left = (int)Math.Round(horizontalOffset);
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
        horizontalOffset = Width;
        textLabel.Left = Width;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            marqueeFont.Dispose();
            privateFonts.Dispose();
            if (bundledFontHandle.IsAllocated)
            {
                bundledFontHandle.Free();
            }
            bundledFontBytes = null;
        }
        base.Dispose(disposing);
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
                ShowMarquee(EventDisplayText(nextEvent));
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
            ShowMarquee(EventDisplayText(nextEvent));
        }
        UpdateStatus(EventDisplayText(nextEvent));
    }

    private void UpdateNextEvent()
    {
        nextEvent = ScheduleReader.FindNext(events, DateTime.Now);
        announcedMinutes.Clear();
        UpdateStatus(nextEvent is null
            ? "今天没有课程"
            : EventDisplayText(nextEvent));
    }

    private static string EventDisplayText(ScheduledEvent scheduledEvent)
    {
        var end = scheduledEvent.End?.ToString("HH:mm") ?? "--:--";
        return $"下一节：{scheduledEvent.Title}（{scheduledEvent.Start:HH:mm}-{end}）";
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
