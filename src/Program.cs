using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets.Providers;
using Microsoft.Windows.Widgets;
using WinRT;

namespace RazerBatteryWidget;

public record Device(string Name, string Category, bool HasBattery, int? Level, string Status);
public record Snapshot(List<Device> Devices, DateTime? Time, string Message);

public static class BatteryReader
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    static void AddReceivers(List<Device> devices)
    {
        using var usb = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1532&PID_00B3");
        int count = usb?.GetSubKeyNames().Count(id => CM_Locate_DevNodeW(out _, @"USB\VID_1532&PID_00B3\" + id, 0) == 0) ?? 0;
        var receivers = devices.Where(d => !d.HasBattery && d.Name.Contains("HYPERPOLLING", StringComparison.OrdinalIgnoreCase)).ToList();
        for (int i = receivers.Count; i < count; i++) devices.Add(new("HYPERPOLLING 无线接收器", "ACCESSORY", false, null, "USB 供电"));
        int index = 0;
        for (int i = 0; i < devices.Count; i++)
            if (!devices[i].HasBattery && devices[i].Name.Contains("HYPERPOLLING", StringComparison.OrdinalIgnoreCase)) devices[i] = devices[i] with { Name = "HYPERPOLLING 接收器 " + (++index) };
    }
    public static Snapshot Read(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Razer", "RazerAppEngine", "User Data", "Logs");
        if (!Directory.Exists(directory)) return new([], null, "未找到雷云日志，请启动 Razer Synapse 4");
        foreach (var file in new DirectoryInfo(directory).GetFiles("systray_systrayv*.log").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, stream.Length - 8 * 1024 * 1024);
                stream.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                if (start > 0) reader.ReadLine();
                var lines = reader.ReadToEnd().Split('\n');
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    int marker = lines[i].IndexOf("devices: ", StringComparison.Ordinal);
                    if (marker < 0) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(lines[i][(marker + 9)..].Trim());
                        var devices = new List<Device>();
                        foreach (var d in doc.RootElement.EnumerateArray())
                        {
                            string name = d.TryGetProperty("name", out var n) && n.TryGetProperty("zh-cn", out var cn) && !string.IsNullOrWhiteSpace(cn.GetString()) ? cn.GetString()! : d.GetProperty("name").GetProperty("en").GetString()!;
                            bool battery = d.TryGetProperty("hasBattery", out var b) && b.ValueKind == JsonValueKind.True;
                            int? level = null;
                            string status = battery ? "电量未知" : "USB 供电";
                            if (battery && d.TryGetProperty("powerStatus", out var power))
                            {
                                if (power.TryGetProperty("level", out var l) && l.TryGetInt32(out var value) && value is >= 0 and <= 100) level = value;
                                string charging = power.TryGetProperty("chargingStatus", out var c) ? c.GetString() ?? "" : "";
                                status = charging.Contains("NoCharge") ? (level == 100 ? "已充满" : "使用中") : charging.Contains("Charging", StringComparison.OrdinalIgnoreCase) || charging == "Charge" ? "正在充电" : "电量状态未知";
                            }
                            devices.Add(new(name, d.TryGetProperty("category", out var cat) ? cat.GetString() ?? "" : "", battery, level, status));
                        }
                        DateTime? timestamp = DateTime.TryParse(lines[i].Substring(1, 23), out var t) ? t : null;
                        AddReceivers(devices);
                        return new(devices.OrderByDescending(d => d.HasBattery).ToList(), timestamp, devices.Count == 0 ? "雷云当前未发现设备" : "");
                    }
                    catch (JsonException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new([], null, "暂无设备数据，请打开雷云后刷新");
    }
}

public static class Card
{
    static object Text(string text, string size = "Default", string weight = "Default", bool subtle = false, string color = "Default") => new { type = "TextBlock", text, size, weight, isSubtle = subtle, color, wrap = true, spacing = "Small", horizontalAlignment = "Center" };
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Images = new();
    static object? Picture(Device device, int height)
    {
        string name = device.Name;
        string? asset = name.Contains("巴塞利斯蛇 V3") || name.Contains("Basilisk V3 Pro 35K", StringComparison.OrdinalIgnoreCase) ? "mouse" : name.Contains("噬魂金蝎 V2") || name.Contains("DeathStalker V2 Pro", StringComparison.OrdinalIgnoreCase) ? "keyboard" : name.Contains("HYPERPOLLING", StringComparison.OrdinalIgnoreCase) ? "receiver" : null;
        if (asset == null) return null;
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Devices", asset + ".png");
        if (!File.Exists(path)) return null;
        string url = Images.GetOrAdd(asset, _ => "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path)));
        return new { type = "Image", url, altText = name, height = height + "px", size = "Auto", horizontalAlignment = "Center", spacing = "None" };
    }
    static object DeviceTile(Device d, bool large)
    {
        var items = new List<object>();
        bool battery = d.HasBattery;
        var picture = Picture(d, battery ? (large ? 112 : 72) : (large ? 52 : 36));
        if (picture != null) items.Add(picture);
        string name = d.Name.Replace("RAZER ", "").Replace("Razer ", "");
        if (!battery) name = name.Replace("HYPERPOLLING 接收器", "接收器");
        items.Add(Text(name, "Small", "Bolder"));
        string color = battery && d.Level <= 20 ? "Attention" : battery ? "Good" : "Default";
        items.Add(Text(battery ? d.Level is int level ? $"{level}%" : "—" : "USB 供电", battery ? "ExtraLarge" : "Small", battery ? "Bolder" : "Default", !battery, color));
        if (large && battery) items.Add(Text(d.Status, "Small", subtle: true));
        return new { type = "Column", width = "stretch", style = "emphasis", verticalContentAlignment = "Center", items };
    }
    public static string Make(Snapshot snapshot, bool large = false)
    {
        var body = new List<object>();
        if (snapshot.Message.Length > 0) body.Add(Text(snapshot.Message, subtle: true));
        foreach (var group in snapshot.Devices.GroupBy(d => d.HasBattery))
        {
            foreach (var pair in group.Chunk(2))
                body.Add(new { type = "ColumnSet", spacing = body.Count == 0 ? "None" : "Medium", columns = pair.Select(d => DeviceTile(d, large)).ToArray() });
        }
        string stamp = snapshot.Time is DateTime t ? $"雷云快照 · {t:HH:mm:ss}" + (DateTime.Now - t > TimeSpan.FromMinutes(5) ? " · 数据可能已过期" : "") : "等待雷云数据";
        body.Add(Text(stamp, "Small", subtle: true));
        return JsonSerializer.Serialize(new { type = "AdaptiveCard", version = "1.5", body, actions = new[] {new {type = "Action.Execute", title = "刷新", verb = "refresh"}} });
    }
}

[Guid("80D85AD8-49FA-4433-B992-08A44879C1B7")]
public class Provider : IWidgetProvider
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Active, bool Large)> Widgets = new();
    static readonly Timer RefreshTimer = new(_ => RefreshActive(), null, 15000, 15000);
    public Provider()
    {
        foreach (var info in WidgetManager.GetDefault().GetWidgetInfos() ?? []) Widgets.TryAdd(info.WidgetContext.Id, (false, info.WidgetContext.Size == WidgetSize.Large));
    }
    static void Update(string id)
    {
        try { WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(id) { Template = Card.Make(BatteryReader.Read(), Widgets.TryGetValue(id, out var widget) && widget.Large), Data = "{}", CustomState = "" }); }
        catch (Exception e) { Program.Log(e.ToString()); }
    }
    static void RefreshActive() { foreach (var pair in Widgets) if (pair.Value.Active) Update(pair.Key); }
    public void CreateWidget(WidgetContext context) { Widgets[context.Id] = (true, context.Size == WidgetSize.Large); Update(context.Id); }
    public void DeleteWidget(string id, string state) { Widgets.TryRemove(id, out _); }
    public void OnActionInvoked(WidgetActionInvokedArgs args) { Update(args.WidgetContext.Id); }
    public void OnWidgetContextChanged(WidgetContextChangedArgs args) { Widgets[args.WidgetContext.Id] = (true, args.WidgetContext.Size == WidgetSize.Large); Update(args.WidgetContext.Id); }
    public void Activate(WidgetContext context) { Widgets[context.Id] = (true, context.Size == WidgetSize.Large); Update(context.Id); }
    public void Deactivate(string id) { if (Widgets.TryGetValue(id, out var widget)) Widgets[id] = (false, widget.Large); }
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance);
    [PreserveSig] int LockServer(bool value);
}
[ComVisible(true)]
class Factory : IClassFactory
{
    static Provider? provider;
    public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
        try
        {
            provider ??= new Provider();
            IntPtr unknown = MarshalInspectable<IWidgetProvider>.FromManaged(provider);
            try { return Marshal.QueryInterface(unknown, ref iid, out instance); }
            finally { Marshal.Release(unknown); }
        }
        catch (Exception e) { Program.Log(e.ToString()); return Marshal.GetHRForException(e); }
    }
    public int LockServer(bool value) => 0;
}
static class Program
{
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint coinit);
    [DllImport("ole32.dll")] static extern int CoRegisterClassObject([MarshalAs(UnmanagedType.LPStruct)] Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint cookie);
    internal static void Log(string message)
    {
        try { var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RazerBatteryWidget"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "provider.log"), DateTime.Now + " " + message + Environment.NewLine); } catch { }
    }
    [MTAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--diagnose")) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostic.json"), JsonSerializer.Serialize(BatteryReader.Read(), new JsonSerializerOptions {WriteIndented=true})); return; }
        if (args.Contains("--card")) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "card.json"), Card.Make(BatteryReader.Read(), args.Contains("--large"))); return; }
        try
        {
            CoInitializeEx(IntPtr.Zero, 0);
            var factory = new Factory();
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(typeof(Provider).GUID, factory, 4, 1, out uint cookie));
            Log("COM provider registered");
            using var wait = new ManualResetEvent(false);
            wait.WaitOne();
            GC.KeepAlive(factory);
        }
        catch(Exception e) { Log(e.ToString()); }
    }
}
