using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RazerBatteryWidget;

public sealed class DoubleClickTracker
{
    readonly Dictionary<string, long> LastClicks = new();
    public bool Click(string id, long now, uint threshold)
    {
        lock (LastClicks)
        {
            if (LastClicks.TryGetValue(id, out long last) && now >= last && now - last <= threshold)
            {
                LastClicks.Remove(id);
                return true;
            }
            LastClicks[id] = now;
            return false;
        }
    }
    public void Reset(string id) { lock (LastClicks) LastClicks.Remove(id); }
}

public static class SynapseLauncher
{
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    public static uint DoubleClickTime => GetDoubleClickTime();
    public static void Open()
    {
        foreach (var root in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
        {
            string shortcut = Path.Combine(Environment.GetFolderPath(root), "Razer", "Razer Synapse.lnk");
            if (File.Exists(shortcut))
            {
                Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true });
                return;
            }
        }
        string engine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Razer", "RazerAppEngine", "RazerAppEngine.exe");
        if (!File.Exists(engine)) throw new FileNotFoundException("未找到 Razer Synapse 4", engine);
        Process.Start(new ProcessStartInfo(engine, "--url-params=apps=synapse") { UseShellExecute = true });
    }
}
