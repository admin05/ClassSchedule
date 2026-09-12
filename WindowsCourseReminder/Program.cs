using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
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

internal sealed record ReminderPalette(string Id, string Name, Color Background, Color Text)
{
    public static IReadOnlyList<ReminderPalette> Presets { get; } =
    [
        new("solarized-light", "浅色 · Solarized Light", Color.FromArgb(253, 246, 227), Color.FromArgb(88, 110, 117)),
        new("catppuccin-latte", "浅色 · Catppuccin Latte", Color.FromArgb(239, 241, 245), Color.FromArgb(76, 79, 105)),
        new("nord-snow", "浅色 · Nord Snow Storm", Color.FromArgb(236, 239, 244), Color.FromArgb(46, 53, 64)),
        new("dracula", "深色 · Dracula", Color.FromArgb(40, 42, 54), Color.FromArgb(248, 248, 242)),
        new("gruvbox-dark", "深色 · Gruvbox Dark", Color.FromArgb(40, 40, 36), Color.FromArgb(235, 219, 178)),
        new("tokyo-night", "深色 · Tokyo Night", Color.FromArgb(26, 27, 38), Color.FromArgb(192, 202, 245))
    ];

    public static ReminderPalette Default => Presets[0];
}

internal sealed record ReminderAppearance(
    string PaletteId,
    Color BackgroundColor,
    Color TextColor,
    string MessageTemplate)
{
    public const string DefaultTemplate = "下一节：{courseName}（{startTime}-{endTime}）";
}

internal static class AppearanceStore
{
    private sealed class SavedAppearance
    {
        [JsonPropertyName("paletteID")]
        public string? PaletteId { get; set; }

        [JsonPropertyName("background")]
        public string? Background { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("template")]
        public string? Template { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassSchedule",
        "reminder-appearance.json");

    public static ReminderAppearance Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return DefaultAppearance();
            }

            var saved = JsonSerializer.Deserialize<SavedAppearance>(File.ReadAllText(SettingsPath));
            var palette = ReminderPalette.Presets.FirstOrDefault(item => item.Id == saved?.PaletteId);
            var template = string.IsNullOrWhiteSpace(saved?.Template)
                ? ReminderAppearance.DefaultTemplate
                : saved!.Template!;
            if (palette is not null)
            {
                return new ReminderAppearance(palette.Id, palette.Background, palette.Text, template);
            }

            if (saved?.PaletteId == "custom")
            {
                return new ReminderAppearance(
                    "custom",
                    ParseColor(saved.Background) ?? ReminderPalette.Default.Background,
                    ParseColor(saved.Text) ?? ReminderPalette.Default.Text,
                    template);
            }
        }
        catch
        {
            // Fall back to the same default used by the macOS implementation.
        }

        return DefaultAppearance();
    }

    public static void Save(ReminderAppearance appearance)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var saved = new SavedAppearance
        {
            PaletteId = appearance.PaletteId,
            Background = ToHex(appearance.BackgroundColor),
            Text = ToHex(appearance.TextColor),
            Template = appearance.MessageTemplate
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(saved, JsonOptions));
    }

    private static ReminderAppearance DefaultAppearance()
    {
        var palette = ReminderPalette.Default;
        return new ReminderAppearance(palette.Id, palette.Background, palette.Text, ReminderAppearance.DefaultTemplate);
    }

    private static string ToHex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color? ParseColor(string? value)
    {
        var hex = value?.Trim().TrimStart('#');
        return hex?.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)
            ? Color.FromArgb(255, (number >> 16) & 0xFF, (number >> 8) & 0xFF, number & 0xFF)
            : null;
    }
}

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

    public MarqueeForm(string text, ReminderAppearance appearance, Action finished)
    {
        this.finished = finished;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = appearance.BackgroundColor;
        ForeColor = appearance.TextColor;
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1440, 900);
        Width = screen.Width;
        Location = new Point(screen.Left, screen.Top + 50);

        marqueeFont = LoadBundledFont();
        textLabel = new Label
        {
            AutoSize = true,
            BackColor = appearance.BackgroundColor,
            ForeColor = appearance.TextColor,
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

    public void ApplyAppearance(string text, ReminderAppearance appearance)
    {
        BackColor = appearance.BackgroundColor;
        ForeColor = appearance.TextColor;
        textLabel.BackColor = appearance.BackgroundColor;
        textLabel.ForeColor = appearance.TextColor;
        textLabel.Text = text;
        horizontalOffset = Width;
        textLabel.Left = Width;
        completedLoops = 0;
        finishing = false;
        UpdateHeight();
        if (Visible)
        {
            animationTimer.Start();
        }
    }

    private void UpdateHeight()
    {
        var textSize = TextRenderer.MeasureText(textLabel.Text, marqueeFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Height = Math.Max(86, textSize.Height + 24);
        textLabel.Top = Math.Max(0, (ClientSize.Height - textLabel.Height) / 2);
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
    private ReminderAppearance appearance = AppearanceStore.Load();
    private AppearanceSettingsForm? appearanceWindow;
    private List<ScheduleEvent> events = [];
    private ScheduledEvent? nextEvent;
    private readonly HashSet<int> announcedMinutes = [];
    private MarqueeForm? activeMarquee;

    public ReminderApplicationContext()
    {
        configPath = Path.Combine(AppContext.BaseDirectory, "课程表.json");
        statusItem = new ToolStripMenuItem("正在读取课程表…") { Enabled = false };
        var appearanceItem = new ToolStripMenuItem("提醒外观设置…");
        appearanceItem.Click += (_, _) => ShowAppearanceSettings();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(appearanceItem);
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

    private string EventDisplayText(ScheduledEvent scheduledEvent)
    {
        var end = scheduledEvent.End?.ToString("HH:mm") ?? "--:--";
        var weekday = scheduledEvent.Start.ToString("dddd", CultureInfo.GetCultureInfo("zh-CN"));
        var replacements = new Dictionary<string, string>
        {
            ["{courseName}"] = scheduledEvent.Title,
            ["{课程名称}"] = scheduledEvent.Title,
            ["{startTime}"] = scheduledEvent.Start.ToString("HH:mm"),
            ["{上课时间}"] = scheduledEvent.Start.ToString("HH:mm"),
            ["{endTime}"] = end,
            ["{下课时间}"] = end,
            ["{weekday}"] = weekday,
            ["{星期}"] = weekday
        };
        return replacements.Aggregate(appearance.MessageTemplate,
            (current, replacement) => current.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal));
    }

    private void ShowMarquee(string text)
    {
        activeMarquee?.Close();
        activeMarquee = new MarqueeForm(text, appearance, FinishMarquee);
        activeMarquee.FormClosed += (_, _) => activeMarquee = null;
        activeMarquee.Show();
    }

    private void ShowAppearanceSettings()
    {
        if (appearanceWindow is { IsDisposed: false })
        {
            appearanceWindow.BringToFront();
            appearanceWindow.Activate();
            return;
        }

        appearanceWindow = new AppearanceSettingsForm(appearance, SaveAppearance);
        appearanceWindow.FormClosed += (_, _) => appearanceWindow = null;
        appearanceWindow.Show();
    }

    private void SaveAppearance(ReminderAppearance updated)
    {
        appearance = updated;
        try
        {
            AppearanceStore.Save(updated);
        }
        catch (Exception error)
        {
            MessageBox.Show($"无法保存提醒外观设置：\n{error.Message}", "课程提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (activeMarquee is not null && nextEvent is not null)
        {
            activeMarquee.ApplyAppearance(EventDisplayText(nextEvent), updated);
        }
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
        appearanceWindow?.Close();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class AppearanceSettingsForm : Form
{
    private readonly ComboBox palettePicker = new();
    private readonly Button backgroundButton = new();
    private readonly Button textButton = new();
    private readonly TextBox templateField = new();
    private readonly Action<ReminderAppearance> onSave;
    private bool updatingPalette;

    public AppearanceSettingsForm(ReminderAppearance appearance, Action<ReminderAppearance> onSave)
    {
        this.onSave = onSave;
        Text = "提醒外观设置";
        ClientSize = new Size(520, 280);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(28, 20),
            Text = "提醒外观"
        };
        Controls.Add(title);

        AddLabel("预设配色", new Point(28, 66));
        palettePicker.DropDownStyle = ComboBoxStyle.DropDownList;
        palettePicker.Location = new Point(150, 62);
        palettePicker.Size = new Size(330, 28);
        palettePicker.Items.AddRange(ReminderPalette.Presets.Select(item => item.Name).Concat(["自定义"]).ToArray());
        palettePicker.SelectedIndexChanged += (_, _) => PaletteChanged();
        Controls.Add(palettePicker);

        AddLabel("背景色", new Point(28, 110));
        ConfigureColorButton(backgroundButton, new Point(150, 106), "背景色", () => ChooseColor(backgroundButton));
        AddLabel("字体色", new Point(270, 110));
        ConfigureColorButton(textButton, new Point(350, 106), "字体色", () => ChooseColor(textButton));

        AddLabel("滚动文字", new Point(28, 154));
        templateField.Location = new Point(150, 150);
        templateField.Size = new Size(330, 28);
        templateField.Text = appearance.MessageTemplate;
        Controls.Add(templateField);

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(150, 184),
            Text = "可用变量：{courseName}  {startTime}  {endTime}  {weekday}"
        };
        Controls.Add(hint);

        var cancel = new Button { DialogResult = DialogResult.Cancel, Location = new Point(300, 224), Size = new Size(84, 32), Text = "取消" };
        var save = new Button { Location = new Point(396, 224), Size = new Size(84, 32), Text = "保存" };
        save.Click += (_, _) => Save();
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(cancel);
        Controls.Add(save);

        var selectedIndex = ReminderPalette.Presets.ToList().FindIndex(item => item.Id == appearance.PaletteId);
        palettePicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : ReminderPalette.Presets.Count;
        backgroundButton.BackColor = appearance.BackgroundColor;
        textButton.BackColor = appearance.TextColor;
    }

    private void AddLabel(string text, Point location)
    {
        Controls.Add(new Label { AutoSize = true, Location = location, Text = text });
    }

    private static void ConfigureColorButton(Button button, Point location, string accessibleName, Action click)
    {
        button.AccessibleName = accessibleName;
        button.FlatStyle = FlatStyle.Standard;
        button.Location = location;
        button.Size = new Size(52, 30);
        button.Text = "…";
        button.UseVisualStyleBackColor = false;
        button.Click += (_, _) => click();
    }

    private void PaletteChanged()
    {
        if (updatingPalette || palettePicker.SelectedIndex >= ReminderPalette.Presets.Count)
        {
            return;
        }

        var palette = ReminderPalette.Presets[palettePicker.SelectedIndex];
        updatingPalette = true;
        backgroundButton.BackColor = palette.Background;
        textButton.BackColor = palette.Text;
        updatingPalette = false;
    }

    private void ChooseColor(Button button)
    {
        using var dialog = new ColorDialog { Color = button.BackColor, FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        button.BackColor = dialog.Color;
        if (!updatingPalette)
        {
            palettePicker.SelectedIndex = ReminderPalette.Presets.Count;
        }
    }

    private void Save()
    {
        var template = templateField.Text.Trim();
        if (template.Length == 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        var paletteId = palettePicker.SelectedIndex < ReminderPalette.Presets.Count
            ? ReminderPalette.Presets[palettePicker.SelectedIndex].Id
            : "custom";
        onSave(new ReminderAppearance(paletteId, backgroundButton.BackColor, textButton.BackColor, template));
        DialogResult = DialogResult.OK;
        Close();
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
