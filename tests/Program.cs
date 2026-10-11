using System.Text.Json;
using RazerBatteryWidget;

if (args.Contains("--open-synapse")) { SynapseLauncher.Open(); Console.WriteLine("Synapse launch sent"); return; }
if (args.Contains("--live"))
{
    Console.WriteLine(JsonSerializer.Serialize(BatteryReader.Read(), new JsonSerializerOptions { WriteIndented = true }));
    return;
}
string folder = Path.Combine(Path.GetTempPath(), "RazerReaderChecks-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
string keyboard = "{\"name\":{\"en\":\"Keyboard\"},\"hasBattery\":true}";
string mouse = "{\"name\":{\"en\":\"Mouse\"},\"hasBattery\":true,\"powerStatus\":{\"level\":98,\"chargingStatus\":\"NoCharge_BatteryFull\"}}";
void Write(string file, string time, string marker, params string[] devices) => File.WriteAllText(Path.Combine(folder, file), $"[2026/10/12 {time}] info: {marker}[{string.Join(',', devices)}]\n");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
var clicks = new DoubleClickTracker();
Check(!clicks.Click("a", 1000, 500), "single click does not launch");
Check(!clicks.Click("b", 1100, 500), "clicks on separate widgets are isolated");
Check(clicks.Click("a", 1300, 500), "two clicks within the system interval launch once");
Check(!clicks.Click("a", 1400, 500), "third click does not launch again");
Check(!clicks.Click("a", 2000, 500), "slow second click does not launch");
clicks.Reset("a");
Check(!clicks.Click("a", 2100, 500), "refresh or deactivation resets pending click");
Write("systray_systrayv1.log", "07:02:06.000", "devices: ", keyboard);
Write("systray_systrayv2.log", "07:03:06.000", "Device  ", keyboard, mouse);
File.SetLastWriteTimeUtc(Path.Combine(folder, "systray_systrayv1.log"), DateTime.UtcNow.AddHours(1));
Check(BatteryReader.Read(folder).Devices.Any(d => d.Name == "Mouse" && d.Level == 98), "post-startup Device event overrides incomplete startup list regardless of file modification order");
Write("dashboard.log", "07:04:06.000", "devices shown in dashboard ", mouse);
Check(!BatteryReader.Read(folder).Devices.Any(d => d.Name == "Keyboard"), "newer dashboard snapshot wins and disconnected devices are not resurrected");
File.AppendAllText(Path.Combine(folder, "dashboard.log"), "[2026/10/12 07:05:06.000] info: devices shown in dashboard [{broken\n");
Check(BatteryReader.Read(folder).Devices.Any(d => d.Name == "Mouse" && d.Level == 98), "partial log write keeps last complete snapshot");
Write("systray_systrayv3.log", "07:06:06.000", "connectingDeviceData: ", keyboard);
var result = BatteryReader.Read(folder);
Check(result.Devices.Any(d => d.Name == "Keyboard" && d.Level == null) && !result.Devices.Any(d => d.Name == "Mouse"), "newest connecting list is authoritative without stale battery or device fallback");
File.AppendAllText(Path.Combine(folder, "systray_systrayv3.log"), "[2026/10/12 07:07:06.000] info: Device  []\n");
Check(!BatteryReader.Read(folder).Devices.Any(d => d.HasBattery), "empty current list removes battery devices even with parsed cache");
Console.WriteLine("Fixtures: " + folder);
