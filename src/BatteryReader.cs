using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RazerBatteryWidget;

public record Device(string Name, string Category, bool HasBattery, int? Level, string Status);
public record Snapshot(List<Device> Devices, DateTime? Time, string Message);

public static class BatteryReader
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    static readonly object Gate = new();
    // Keep parsed snapshots only; never retain the device serials or raw logs.
    static readonly Dictionary<string, (long Length, DateTime Modified, Snapshot? Snapshot)> Cache = new();
    static readonly string[] Markers = ["devices: ", "connectingDeviceData: ", "Device  ", "devices shown in dashboard "];

    static void AddReceivers(List<Device> devices)
    {
        using var usb = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1532&PID_00B3");
        int count = usb?.GetSubKeyNames().Count(id => CM_Locate_DevNodeW(out _, @"USB\VID_1532&PID_00B3\" + id, 0) == 0) ?? 0;
        int existing = devices.Count(d => !d.HasBattery && d.Name.Contains("HYPERPOLLING", StringComparison.OrdinalIgnoreCase));
        for (int i = existing; i < count; i++) devices.Add(new("HYPERPOLLING 无线接收器", "ACCESSORY", false, null, "USB 供电"));
        int index = 0;
        for (int i = 0; i < devices.Count; i++)
            if (!devices[i].HasBattery && devices[i].Name.Contains("HYPERPOLLING", StringComparison.OrdinalIgnoreCase)) devices[i] = devices[i] with { Name = "HYPERPOLLING 接收器 " + (++index) };
    }

    static Snapshot? Parse(string line)
    {
        int start = -1;
        foreach (string marker in Markers)
        {
            int index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0) { start = index + marker.Length; break; }
        }
        if (start < 0 || line.Length < 25 || !DateTime.TryParseExact(line.Substring(1, 23), "yyyy/MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line[start..].Trim());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var devices = new List<Device>();
            foreach (var d in doc.RootElement.EnumerateArray())
            {
                if (!d.TryGetProperty("name", out var names) || names.ValueKind != JsonValueKind.Object) continue;
                string? name = names.TryGetProperty("zh-cn", out var cn) && cn.ValueKind == JsonValueKind.String ? cn.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) name = names.TryGetProperty("en", out var en) && en.ValueKind == JsonValueKind.String ? en.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                bool battery = d.TryGetProperty("hasBattery", out var b) && b.ValueKind == JsonValueKind.True;
                int? level = null;
                string status = battery ? "电量未知" : "USB 供电";
                if (battery && d.TryGetProperty("powerStatus", out var power) && power.ValueKind == JsonValueKind.Object)
                {
                    if (power.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var value) && value is >= 0 and <= 100) level = value;
                    string charging = power.TryGetProperty("chargingStatus", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                    status = charging.Contains("NoCharge") ? (level == 100 ? "已充满" : "使用中") : charging.Contains("Charging", StringComparison.OrdinalIgnoreCase) || charging == "Charge" ? "正在充电" : "电量状态未知";
                }
                devices.Add(new(name, d.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String ? cat.GetString() ?? "" : "", battery, level, status));
            }
            return new(devices, time, "");
        }
        catch (JsonException) { return null; }
    }

    static Snapshot? LatestInFile(FileInfo file)
    {
        if (Cache.TryGetValue(file.FullName, out var cached) && cached.Length == file.Length && cached.Modified == file.LastWriteTimeUtc) return cached.Snapshot;
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        long start = Math.Max(0, length - 8 * 1024 * 1024);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        if (start > 0) reader.ReadLine();
        Snapshot? latest = null;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var snapshot = Parse(line);
            if (snapshot != null && (latest == null || snapshot.Time >= latest.Time)) latest = snapshot;
        }
        Cache[file.FullName] = (length, file.LastWriteTimeUtc, latest);
        return latest;
    }

    public static Snapshot Read(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Razer", "RazerAppEngine", "User Data", "Logs");
        if (!Directory.Exists(directory)) return new([], null, "未找到雷云日志，请启动 Razer Synapse 4");
        lock (Gate)
        {
            Snapshot? latest = null;
            // Rotation and startup can update file timestamps out of order. Compare the
            // timestamps inside complete device lists, rather than returning the first file.
            var files = new DirectoryInfo(directory).GetFiles("*.log").Where(f => f.Name.StartsWith("systray_systrayv", StringComparison.Ordinal) || f.Name.StartsWith("dashboard", StringComparison.Ordinal)).ToArray();
            var paths = files.Select(f => f.FullName).ToHashSet();
            foreach (var key in Cache.Keys.Where(k => !paths.Contains(k)).ToArray()) Cache.Remove(key);
            foreach (var file in files)
            {
                try
                {
                    var snapshot = LatestInFile(file);
                    if (snapshot != null && (latest == null || snapshot.Time > latest.Time)) latest = snapshot;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (latest == null) return new([], null, "暂无设备数据，请打开雷云后刷新");
            // Do not merge older device lists: a missing device may have disconnected.
            var devices = latest.Devices.ToList();
            AddReceivers(devices);
            return new(devices.OrderByDescending(d => d.HasBattery).ToList(), latest.Time, devices.Count == 0 ? "雷云当前未发现设备" : "");
        }
    }
}
