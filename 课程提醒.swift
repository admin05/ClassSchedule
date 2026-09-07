import Cocoa
import Foundation

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
    private let text: String
    private let textAttributes: [NSAttributedString.Key: Any]
    private var textWidth: CGFloat = 0
    private var offset: CGFloat = 0
    private var completedLoops = 0
    private var timer: Timer?
    private let onFinished: () -> Void

    init(text: String, onFinished: @escaping () -> Void) {
        self.text = text
        self.onFinished = onFinished
        self.textAttributes = [
            .font: NSFont.systemFont(ofSize: 30, weight: .semibold),
            .foregroundColor: NSColor.white
        ]
        super.init(frame: .zero)
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        layer?.cornerRadius = 12
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard window != nil else { return }
        let measured = (text as NSString).size(withAttributes: textAttributes)
        textWidth = measured.width
        offset = bounds.width
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in
            self?.advance()
        }
    }

    private func advance() {
        offset -= 2
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

private final class ReminderController: NSObject, NSApplicationDelegate {
    private var events: [ScheduleEvent] = []
    private var nextEvent: ScheduledEvent?
    private var announcedMinutes = Set<Int>()
    private var checkTimer: Timer?
    private var statusItem: NSStatusItem!
    private var statusMenu: NSMenu!
    private var announcementWindow: NSWindow?

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
        return "下一节：\(event.title)（\(start)-\(end)）"
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
        let marquee = MarqueeView(text: text) { [weak self] in
            self?.finishAnnouncement()
        }
        marquee.frame = NSRect(x: 0, y: 0, width: width, height: height)
        window.contentView = marquee
        announcementWindow = window
        window.orderFrontRegardless()
    }

    private func finishAnnouncement() {
        announcementWindow?.orderOut(nil)
        announcementWindow = nil
    }

    private func showError(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "课程提醒启动失败"
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.runModal()
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
    let app = NSApplication.shared
    let delegate = ReminderController()
    app.delegate = delegate
    app.run()
}
