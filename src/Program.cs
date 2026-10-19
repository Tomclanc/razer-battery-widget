using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets.Providers;
using Microsoft.Windows.Widgets;
using WinRT;

namespace RazerBatteryWidget;

public static class Card
{
    static object Text(string text, string size = "Default", string weight = "Default", bool subtle = false, string color = "Default") => new { type = "TextBlock", text, size, weight, isSubtle = subtle, color, wrap = true, spacing = "Small", horizontalAlignment = "Center" };
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Images = new();
    static string DisplayName(Device d)
    {
        string name = d.Name;
        if (name.Contains("炼狱蝰蛇 V3") || name.Contains("DeathAdder V3 Pro", StringComparison.OrdinalIgnoreCase)) return "炼狱蝰蛇 V3 Pro";
        if (name.Contains("巴塞利斯蛇 V3") || name.Contains("Basilisk V3 Pro", StringComparison.OrdinalIgnoreCase)) return "巴塞利斯蛇 V3 Pro";
        if (name.Contains("噬魂金蝎 V2") || name.Contains("DeathStalker V2 Pro", StringComparison.OrdinalIgnoreCase)) return "噬魂金蝎 V2";
        name = name.Replace("RAZER", "", StringComparison.OrdinalIgnoreCase).Trim();
        return d.HasBattery ? name : name.Replace("HYPERPOLLING 接收器", "接收器");
    }
    static object? Picture(Device device, int height)
    {
        string name = device.Name;
        bool deathAdder = name.Contains("炼狱蝰蛇 V3") || name.Contains("DeathAdder V3 Pro", StringComparison.OrdinalIgnoreCase);
        string? asset = deathAdder
            ? (name.Contains("白色") || name.Contains("White", StringComparison.OrdinalIgnoreCase) ? "deathadder-white" : "deathadder-black")
            : name.Contains("巴塞利斯蛇 V3") || name.Contains("Basilisk V3 Pro 35K", StringComparison.OrdinalIgnoreCase) ? "mouse" : name.Contains("噬魂金蝎 V2") || name.Contains("DeathStalker V2 Pro", StringComparison.OrdinalIgnoreCase) ? "keyboard" : name.Contains("HYPERPOLLING", StringComparison.OrdinalIgnoreCase) ? "receiver" : null;
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
        var picture = Picture(d, battery ? (large ? 88 : 64) : (large ? 44 : 32));
        if (picture != null) items.Add(picture);
        items.Add(new { type = "TextBlock", text = DisplayName(d), size = "Small", weight = "Bolder", wrap = false, spacing = "Small", horizontalAlignment = "Center" });
        string color = battery && d.Level <= 20 ? "Attention" : battery ? "Good" : "Default";
        items.Add(Text(battery ? d.Level is int level ? $"{level}%" : "—" : "USB 供电", battery ? "ExtraLarge" : "Small", battery ? "Bolder" : "Default", !battery, color));
        if (large && battery) items.Add(Text(d.Status, "Small", subtle: true));
        return new { type = "Column", width = "stretch", verticalContentAlignment = "Top", items };
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
        return JsonSerializer.Serialize(new { type = "AdaptiveCard", version = "1.5", body, selectAction = new { type = "Action.Execute", verb = "deviceClick", tooltip = "双击打开雷云" }, actions = new[] {new {type = "Action.Execute", title = "刷新", verb = "refresh"}} });
    }
}

[Guid("80D85AD8-49FA-4433-B992-08A44879C1B7")]
public class Provider : IWidgetProvider
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Active, bool Large)> Widgets = new();
    static readonly Timer RefreshTimer = new(_ => RefreshActive(), null, 15000, 15000);
    static readonly DoubleClickTracker Clicks = new();
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
    public void DeleteWidget(string id, string state) { Widgets.TryRemove(id, out _); Clicks.Reset(id); }
    public void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        string id = args.WidgetContext.Id;
        if (args.Verb == "deviceClick")
        {
            // Leave the card unchanged after the first click so the host can send
            // the second action on the same surface.
            if (Clicks.Click(id, Environment.TickCount64, SynapseLauncher.DoubleClickTime))
                try { SynapseLauncher.Open(); } catch (Exception e) { Program.Log("Open Synapse: " + e); }
            return;
        }
        Clicks.Reset(id);
        if (args.Verb == "refresh") Update(id);
    }
    public void OnWidgetContextChanged(WidgetContextChangedArgs args) { Widgets[args.WidgetContext.Id] = (true, args.WidgetContext.Size == WidgetSize.Large); Update(args.WidgetContext.Id); }
    public void Activate(WidgetContext context) { Widgets[context.Id] = (true, context.Size == WidgetSize.Large); Update(context.Id); }
    public void Deactivate(string id) { Clicks.Reset(id); if (Widgets.TryGetValue(id, out var widget)) Widgets[id] = (false, widget.Large); }
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
