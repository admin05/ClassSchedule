import Cocoa
import CoreText
import Foundation

private let bundledMarqueeFontName = "LXGW WenKai Mono Screen"

private struct ReminderPalette {
    let id: String
    let name: String
    let background: NSColor
    let text: NSColor

    static let presets: [ReminderPalette] = [
        // Palette references: Solarized, Catppuccin, Nord, Dracula, Gruvbox and Tokyo Night.
        ReminderPalette(id: "solarized-light", name: "浅色 · Solarized Light", background: NSColor(srgbRed: 0.992, green: 0.965, blue: 0.890, alpha: 1), text: NSColor(srgbRed: 0.345, green: 0.431, blue: 0.455, alpha: 1)),
        ReminderPalette(id: "catppuccin-latte", name: "浅色 · Catppuccin Latte", background: NSColor(srgbRed: 0.937, green: 0.945, blue: 0.961, alpha: 1), text: NSColor(srgbRed: 0.298, green: 0.310, blue: 0.412, alpha: 1)),
        ReminderPalette(id: "nord-snow", name: "浅色 · Nord Snow Storm", background: NSColor(srgbRed: 0.925, green: 0.937, blue: 0.957, alpha: 1), text: NSColor(srgbRed: 0.180, green: 0.208, blue: 0.251, alpha: 1)),
        ReminderPalette(id: "dracula", name: "深色 · Dracula", background: NSColor(srgbRed: 0.157, green: 0.165, blue: 0.212, alpha: 1), text: NSColor(srgbRed: 0.973, green: 0.973, blue: 0.973, alpha: 1)),
        ReminderPalette(id: "gruvbox-dark", name: "深色 · Gruvbox Dark", background: NSColor(srgbRed: 0.157, green: 0.157, blue: 0.141, alpha: 1), text: NSColor(srgbRed: 0.922, green: 0.859, blue: 0.698, alpha: 1)),
        ReminderPalette(id: "tokyo-night", name: "深色 · Tokyo Night", background: NSColor(srgbRed: 0.102, green: 0.106, blue: 0.149, alpha: 1), text: NSColor(srgbRed: 0.753, green: 0.792, blue: 0.961, alpha: 1))
    ]

    static let defaultPalette = presets[0]
}

private struct ReminderAppearance {
    static let defaultTemplate = "下一节：{courseName}（{startTime}-{endTime}）"

    var paletteID: String
    var backgroundColor: NSColor
    var textColor: NSColor
    var messageTemplate: String
}

private func colorHex(_ color: NSColor) -> String {
    guard let rgb = color.usingColorSpace(.sRGB) else { return "FFFFFF" }
    let red = Int((rgb.redComponent * 255).rounded())
    let green = Int((rgb.greenComponent * 255).rounded())
    let blue = Int((rgb.blueComponent * 255).rounded())
    return String(format: "%02X%02X%02X", red, green, blue)
}

private func colorFromHex(_ value: String) -> NSColor? {
    let hex = value.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: "#", with: "")
    guard hex.count == 6, let number = Int(hex, radix: 16) else { return nil }
    return NSColor(
        srgbRed: CGFloat((number >> 16) & 0xFF) / 255,
        green: CGFloat((number >> 8) & 0xFF) / 255,
        blue: CGFloat(number & 0xFF) / 255,
        alpha: 1
    )
}

private enum AppearanceStore {
    private static let paletteKey = "reminder.appearance.palette"
    private static let backgroundKey = "reminder.appearance.background"
    private static let textKey = "reminder.appearance.text"
    private static let templateKey = "reminder.appearance.template"

    static func load() -> ReminderAppearance {
        let defaults = UserDefaults.standard
        let storedPaletteID = defaults.string(forKey: paletteKey)
        let isPreset = storedPaletteID.map { id in ReminderPalette.presets.contains(where: { $0.id == id }) } ?? false
        let paletteID = isPreset ? storedPaletteID! : (storedPaletteID == "custom" ? "custom" : ReminderPalette.defaultPalette.id)
        let palette = ReminderPalette.presets.first(where: { $0.id == paletteID }) ?? ReminderPalette.defaultPalette
        return ReminderAppearance(
            paletteID: paletteID,
            backgroundColor: isPreset ? palette.background : (defaults.string(forKey: backgroundKey).flatMap(colorFromHex) ?? palette.background),
            textColor: isPreset ? palette.text : (defaults.string(forKey: textKey).flatMap(colorFromHex) ?? palette.text),
            messageTemplate: defaults.string(forKey: templateKey).flatMap { $0.isEmpty ? nil : $0 } ?? ReminderAppearance.defaultTemplate
        )
    }

    static func save(_ appearance: ReminderAppearance) {
        let defaults = UserDefaults.standard
        defaults.set(appearance.paletteID, forKey: paletteKey)
        defaults.set(colorHex(appearance.backgroundColor), forKey: backgroundKey)
        defaults.set(colorHex(appearance.textColor), forKey: textKey)
        defaults.set(appearance.messageTemplate, forKey: templateKey)
    }
}

private func registerBundledMarqueeFont() {
    guard let fontURL = Bundle.main.url(forResource: "LXGWWenKaiMonoScreen", withExtension: "ttf") else {
        return
    }
    var registrationError: Unmanaged<CFError>?
    CTFontManagerRegisterFontsForURL(fontURL as CFURL, .process, &registrationError)
}

private struct ScheduleEvent: Decodable {
    let title: String
    let weekday: String?
    let dayOfWeek: Int?
    let startTime: String
    let endTime: String?
}

private struct ScheduleFile: Decodable {
    let calendarEvents: [ScheduleEvent]
}

private struct ScheduledEvent: Equatable {
    let title: String
    let start: Date
    let end: Date?
}

private func weekdayIndex(for event: ScheduleEvent) -> Int? {
    if let dayOfWeek = event.dayOfWeek {
        if (0...6).contains(dayOfWeek) { return dayOfWeek }
        if (1...7).contains(dayOfWeek) { return dayOfWeek % 7 }
    }

    guard let weekday = event.weekday?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() else {
        return nil
    }
    let aliases: [String: Int] = [
        "星期日": 0, "星期天": 0, "周日": 0, "周天": 0, "日": 0, "天": 0,
        "sunday": 0, "sun": 0, "星期一": 1, "周一": 1, "一": 1, "monday": 1, "mon": 1,
        "星期二": 2, "周二": 2, "二": 2, "tuesday": 2, "tue": 2,
        "星期三": 3, "周三": 3, "三": 3, "wednesday": 3, "wed": 3,
        "星期四": 4, "周四": 4, "四": 4, "thursday": 4, "thu": 4,
        "星期五": 5, "周五": 5, "五": 5, "friday": 5, "fri": 5,
        "星期六": 6, "周六": 6, "六": 6, "saturday": 6, "sat": 6
    ]
    return aliases[weekday]
}

private func timeComponents(from value: String) -> (hour: Int, minute: Int)? {
    let parts = value.split(separator: ":")
    guard parts.count >= 2,
          let hour = Int(parts[0]), let minute = Int(parts[1]),
          (0...23).contains(hour), (0...59).contains(minute) else {
        return nil
    }
    return (hour, minute)
}

private func configURL() -> URL {
    let appURL = Bundle.main.bundleURL
    let adjacentURL = appURL.deletingLastPathComponent().appendingPathComponent("课程表.json")
    if FileManager.default.fileExists(atPath: adjacentURL.path) {
        return adjacentURL
    }
    return Bundle.main.url(forResource: "课程表", withExtension: "json") ?? adjacentURL
}

private func loadSchedule() throws -> ScheduleFile {
    let url = configURL()
    let data = try Data(contentsOf: url)
    return try JSONDecoder().decode(ScheduleFile.self, from: data)
}

private func nextScheduledEvent(events: [ScheduleEvent], after now: Date = Date()) -> ScheduledEvent? {
    let calendar = Calendar.autoupdatingCurrent
    let today = calendar.startOfDay(for: now)
    let currentWeekday = calendar.component(.weekday, from: today) - 1 // Sunday = 0

    var candidates: [ScheduledEvent] = []
    for event in events {
        guard let weekday = weekdayIndex(for: event),
              let startTime = timeComponents(from: event.startTime) else { continue }

        var dayOffset = (weekday - currentWeekday + 7) % 7
        var date = calendar.date(byAdding: .day, value: dayOffset, to: today) ?? today
        date = calendar.date(bySettingHour: startTime.hour, minute: startTime.minute, second: 0, of: date) ?? date
        if date <= now {
            dayOffset += 7
            date = calendar.date(byAdding: .day, value: dayOffset, to: today) ?? date
            date = calendar.date(bySettingHour: startTime.hour, minute: startTime.minute, second: 0, of: date) ?? date
        }

        var endDate: Date?
        if let endTime = event.endTime.flatMap(timeComponents(from:)) {
            endDate = calendar.date(bySettingHour: endTime.hour, minute: endTime.minute, second: 0, of: date)
            if let resolvedEndDate = endDate, resolvedEndDate <= date {
                endDate = calendar.date(byAdding: .day, value: 1, to: resolvedEndDate)
            }
        }
        candidates.append(ScheduledEvent(title: event.title, start: date, end: endDate))
    }
    return candidates.min { $0.start < $1.start }
}

private final class MarqueeView: NSView {
    private var text: String
    private var textAttributes: [NSAttributedString.Key: Any]
    private var backgroundColor: NSColor
    private var textWidth: CGFloat = 0
    private var offset: CGFloat = 0
    private let scrollSpeed: CGFloat = 2.2
    private var completedLoops = 0
    private var timer: Timer?
    private let onFinished: () -> Void

    init(text: String, appearance: ReminderAppearance, onFinished: @escaping () -> Void) {
        self.text = text
        self.backgroundColor = appearance.backgroundColor
        self.onFinished = onFinished
        let marqueeFont = NSFont(name: bundledMarqueeFontName, size: 30)
            ?? NSFont.systemFont(ofSize: 30, weight: .semibold)
        self.textAttributes = [
            .font: marqueeFont,
            .foregroundColor: appearance.textColor
        ]
        super.init(frame: .zero)
        wantsLayer = true
        layer?.backgroundColor = backgroundColor.cgColor
        layer?.cornerRadius = 12
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard window != nil else {
            timer?.invalidate()
            timer = nil
            return
        }
        let measured = (text as NSString).size(withAttributes: textAttributes)
        textWidth = measured.width
        offset = bounds.width
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in
            self?.advance()
        }
    }

    func apply(text: String, appearance: ReminderAppearance) {
        self.text = text
        let marqueeFont = NSFont(name: bundledMarqueeFontName, size: 30)
            ?? NSFont.systemFont(ofSize: 30, weight: .semibold)
        textAttributes = [
            .font: marqueeFont,
            .foregroundColor: appearance.textColor
        ]
        backgroundColor = appearance.backgroundColor
        layer?.backgroundColor = backgroundColor.cgColor
        textWidth = (text as NSString).size(withAttributes: textAttributes).width
        offset = bounds.width
        completedLoops = 0
        needsDisplay = true
    }

    deinit {
        timer?.invalidate()
    }

    private func advance() {
        offset -= scrollSpeed
        if offset + textWidth < 0 {
            completedLoops += 1
            if completedLoops >= 3 {
                timer?.invalidate()
                timer = nil
                // Finish on the next run-loop turn so the view is not released
                // while the current animation callback is still unwinding.
                let completion = onFinished
                DispatchQueue.main.async(execute: completion)
                return
            }
            offset = bounds.width
        }
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        let y = (bounds.height - 36) / 2
        (text as NSString).draw(at: NSPoint(x: offset, y: y), withAttributes: textAttributes)
    }
}

private final class AppearanceWindowController: NSObject {
    private let appearance: ReminderAppearance
    private let onSave: (ReminderAppearance) -> Void
    private let palettePopup = NSPopUpButton(frame: .zero, pullsDown: false)
    private let backgroundWell = NSColorWell(frame: .zero)
    private let textWell = NSColorWell(frame: .zero)
    private let templateField = NSTextField(frame: .zero)
    private var updatingPalette = false
    private var window: NSWindow?

    init(appearance: ReminderAppearance, onSave: @escaping (ReminderAppearance) -> Void) {
        self.appearance = appearance
        self.onSave = onSave
        super.init()
    }

    func show() {
        if let window {
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }

        let content = NSView(frame: NSRect(x: 0, y: 0, width: 520, height: 300))
        let title = NSTextField(labelWithString: "提醒外观")
        title.font = NSFont.boldSystemFont(ofSize: 18)
        title.frame = NSRect(x: 28, y: 252, width: 460, height: 24)
        content.addSubview(title)

        let paletteLabel = NSTextField(labelWithString: "预设配色")
        paletteLabel.frame = NSRect(x: 28, y: 208, width: 110, height: 22)
        content.addSubview(paletteLabel)
        palettePopup.frame = NSRect(x: 150, y: 204, width: 330, height: 28)
        palettePopup.removeAllItems()
        palettePopup.addItems(withTitles: ReminderPalette.presets.map(\.name) + ["自定义"])
        let selectedIndex = ReminderPalette.presets.firstIndex(where: { $0.id == appearance.paletteID }) ?? ReminderPalette.presets.count
        palettePopup.selectItem(at: selectedIndex)
        palettePopup.target = self
        palettePopup.action = #selector(paletteChanged)
        content.addSubview(palettePopup)

        let backgroundLabel = NSTextField(labelWithString: "背景色")
        backgroundLabel.frame = NSRect(x: 28, y: 164, width: 110, height: 22)
        content.addSubview(backgroundLabel)
        backgroundWell.frame = NSRect(x: 150, y: 158, width: 52, height: 32)
        backgroundWell.color = appearance.backgroundColor
        backgroundWell.target = self
        backgroundWell.action = #selector(colorWellChanged)
        content.addSubview(backgroundWell)

        let textLabel = NSTextField(labelWithString: "字体色")
        textLabel.frame = NSRect(x: 270, y: 164, width: 70, height: 22)
        content.addSubview(textLabel)
        textWell.frame = NSRect(x: 350, y: 158, width: 52, height: 32)
        textWell.color = appearance.textColor
        textWell.target = self
        textWell.action = #selector(colorWellChanged)
        content.addSubview(textWell)

        let templateLabel = NSTextField(labelWithString: "滚动文字")
        templateLabel.frame = NSRect(x: 28, y: 116, width: 110, height: 22)
        content.addSubview(templateLabel)
        templateField.frame = NSRect(x: 150, y: 110, width: 330, height: 28)
        templateField.stringValue = appearance.messageTemplate
        templateField.placeholderString = ReminderAppearance.defaultTemplate
        content.addSubview(templateField)

        let hint = NSTextField(labelWithString: "可用变量：{courseName}  {startTime}  {endTime}  {weekday}")
        hint.font = NSFont.systemFont(ofSize: 12)
        hint.textColor = .secondaryLabelColor
        hint.frame = NSRect(x: 150, y: 82, width: 340, height: 20)
        content.addSubview(hint)

        let cancel = NSButton(title: "取消", target: self, action: #selector(cancelPressed))
        cancel.bezelStyle = .rounded
        cancel.frame = NSRect(x: 300, y: 24, width: 84, height: 32)
        content.addSubview(cancel)
        let save = NSButton(title: "保存", target: self, action: #selector(savePressed))
        save.keyEquivalent = "\r"
        save.bezelStyle = .rounded
        save.frame = NSRect(x: 396, y: 24, width: 84, height: 32)
        content.addSubview(save)

        let newWindow = NSWindow(contentRect: content.frame, styleMask: [.titled, .closable], backing: .buffered, defer: false)
        newWindow.title = "提醒外观设置"
        newWindow.contentView = content
        newWindow.isReleasedWhenClosed = false
        window = newWindow
        newWindow.center()
        newWindow.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func paletteChanged() {
        guard palettePopup.indexOfSelectedItem < ReminderPalette.presets.count else { return }
        let palette = ReminderPalette.presets[palettePopup.indexOfSelectedItem]
        updatingPalette = true
        backgroundWell.color = palette.background
        textWell.color = palette.text
        updatingPalette = false
    }

    @objc private func colorWellChanged() {
        guard !updatingPalette else { return }
        palettePopup.selectItem(at: ReminderPalette.presets.count)
    }

    @objc private func cancelPressed() {
        window?.close()
    }

    @objc private func savePressed() {
        let template = templateField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !template.isEmpty else {
            NSSound.beep()
            return
        }
        let selectedPalette = palettePopup.indexOfSelectedItem < ReminderPalette.presets.count
            ? ReminderPalette.presets[palettePopup.indexOfSelectedItem]
            : nil
        let saved = ReminderAppearance(
            paletteID: selectedPalette?.id ?? "custom",
            backgroundColor: backgroundWell.color,
            textColor: textWell.color,
            messageTemplate: template
        )
        onSave(saved)
        window?.close()
    }

}

private final class ReminderController: NSObject, NSApplicationDelegate {
    private var events: [ScheduleEvent] = []
    private var nextEvent: ScheduledEvent?
    private var announcedMinutes = Set<Int>()
    private var checkTimer: Timer?
    private var statusItem: NSStatusItem!
    private var statusMenu: NSMenu!
    private var announcementWindow: NSWindow?
    private weak var activeMarquee: MarqueeView?
    private var activeAnnouncementText: String?
    private var activeAnnouncementID: UUID?
    private var appearance = AppearanceStore.load()
    private var appearanceWindow: AppearanceWindowController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Keep a Dock icon as a fallback when the MacBook menu bar notch hides status items.
        NSApp.setActivationPolicy(.regular)
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let image = NSImage(systemSymbolName: "bell.fill", accessibilityDescription: "课程提醒") {
            image.isTemplate = true
            statusItem.button?.image = image
        } else {
            statusItem.button?.title = "课"
        }
        statusItem.button?.toolTip = "课程提醒"
        statusMenu = NSMenu()
        statusMenu.addItem(NSMenuItem(title: "正在读取课程表…", action: nil, keyEquivalent: ""))
        let appearanceItem = NSMenuItem(title: "提醒外观设置…", action: #selector(showAppearanceSettings), keyEquivalent: ",")
        appearanceItem.target = self
        statusMenu.addItem(appearanceItem)
        statusMenu.addItem(NSMenuItem.separator())
        statusMenu.addItem(NSMenuItem(title: "退出", action: #selector(quit), keyEquivalent: "q"))
        statusItem.menu = statusMenu

        do {
            events = try loadSchedule().calendarEvents
            guard !events.isEmpty else {
                updateStatus("课程表为空")
                return
            }
            updateNextEvent()
            if let event = nextEvent, event.start.timeIntervalSinceNow > 5 * 60 {
                showNextEventPreview(for: event)
            }
            checkTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
                self?.checkSchedule()
            }
        } catch {
            updateStatus("读取失败")
            showError("无法读取课程表.json：\n\(error.localizedDescription)")
        }
    }

    private func checkSchedule() {
        guard let event = nextEvent else {
            updateNextEvent()
            return
        }
        let remaining = event.start.timeIntervalSinceNow
        if remaining <= 0 {
            updateNextEvent()
            return
        }

        let minutesBefore = Int(ceil(remaining / 60.0))
        if (1...5).contains(minutesBefore), !announcedMinutes.contains(minutesBefore) {
            announcedMinutes.insert(minutesBefore)
            showAnnouncement(for: event)
        }
        updateStatus(eventDisplayText(for: event))
    }

    private func updateNextEvent() {
        nextEvent = nextScheduledEvent(events: events)
        announcedMinutes.removeAll()
        if let event = nextEvent {
            updateStatus(eventDisplayText(for: event))
        } else {
            updateStatus("今天没有课程")
        }
    }

    private func eventDisplayText(for event: ScheduledEvent) -> String {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm"
        let start = formatter.string(from: event.start)
        let end = event.end.map(formatter.string(from:)) ?? "--:--"
        let weekdayFormatter = DateFormatter()
        weekdayFormatter.locale = Locale(identifier: "zh_CN")
        weekdayFormatter.dateFormat = "EEEE"
        let values = [
            "{courseName}": event.title,
            "{课程名称}": event.title,
            "{startTime}": start,
            "{上课时间}": start,
            "{endTime}": end,
            "{下课时间}": end,
            "{weekday}": weekdayFormatter.string(from: event.start),
            "{星期}": weekdayFormatter.string(from: event.start)
        ]
        return values.reduce(appearance.messageTemplate) { result, replacement in
            result.replacingOccurrences(of: replacement.key, with: replacement.value)
        }
    }

    private func updateStatus(_ title: String) {
        guard let first = statusMenu?.items.first else { return }
        first.title = title
    }

    private func showNextEventPreview(for event: ScheduledEvent) {
        showAnnouncement(text: eventDisplayText(for: event))
    }

    private func showAnnouncement(for event: ScheduledEvent) {
        showAnnouncement(text: eventDisplayText(for: event))
    }

    private func showAnnouncement(text: String) {
        announcementWindow?.close()
        activeMarquee = nil
        activeAnnouncementText = text
        let announcementID = UUID()
        activeAnnouncementID = announcementID
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let width = screen.width
        let height: CGFloat = 82
        let frame = NSRect(x: screen.minX, y: screen.maxY - height - 70, width: width, height: height)
        let window = NSWindow(contentRect: frame, styleMask: .borderless, backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.ignoresMouseEvents = true
        let marquee = MarqueeView(text: text, appearance: appearance) { [weak self] in
            self?.finishAnnouncement(id: announcementID)
        }
        marquee.frame = NSRect(x: 0, y: 0, width: width, height: height)
        window.contentView = marquee
        announcementWindow = window
        activeMarquee = marquee
        window.orderFrontRegardless()
    }

    private func finishAnnouncement(id: UUID) {
        guard id == activeAnnouncementID else { return }
        announcementWindow?.orderOut(nil)
        announcementWindow = nil
        activeMarquee = nil
        activeAnnouncementText = nil
        activeAnnouncementID = nil
    }

    private func showError(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "课程提醒启动失败"
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.runModal()
    }

    @objc private func showAppearanceSettings() {
        let controller = AppearanceWindowController(appearance: appearance) { [weak self] updated in
            guard let self else { return }
            self.appearance = updated
            AppearanceStore.save(updated)
            if self.activeMarquee != nil, let event = self.nextEvent {
                let updatedText = self.eventDisplayText(for: event)
                self.activeAnnouncementText = updatedText
                self.activeMarquee?.apply(text: updatedText, appearance: updated)
            } else if let activeAnnouncementText = self.activeAnnouncementText {
                self.activeMarquee?.apply(text: activeAnnouncementText, appearance: updated)
            }
        }
        appearanceWindow = controller
        controller.show()
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}

if CommandLine.arguments.contains("--check") {
    do {
        let schedule = try loadSchedule()
        if let next = nextScheduledEvent(events: schedule.calendarEvents) {
            let formatter = DateFormatter()
            formatter.dateFormat = "yyyy-MM-dd HH:mm"
            print("下一节课程：\(next.title) @ \(formatter.string(from: next.start))")
        } else {
            print("没有找到有效的课程安排")
        }
    } catch {
        fputs("读取失败：\(error.localizedDescription)\n", stderr)
        exit(1)
    }
} else {
    registerBundledMarqueeFont()
    let app = NSApplication.shared
    let delegate = ReminderController()
    app.delegate = delegate
    app.run()
}
