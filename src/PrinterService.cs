using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PrinterToolkit
{
    internal class PrinterInfo
    {
        public string Name, DriverName, PortName, ShareName, Location, Comment, HostAddress;
        public uint Status, ErrorState;
        public bool WorkOffline, IsDefault, IsNetwork, IsShared;
        public int JobCount;
        public PrinterProfile Profile;
        public bool IsUsb { get { return PortName != null && PortName.StartsWith("USB", StringComparison.OrdinalIgnoreCase); } }
    }

    internal class DriverInfo
    {
        public string Name, Version, FileVersion, DriverPath, InfPath, Manufacturer, Provider, DriverDate, Environment, OemUrl;
        public bool Found;
        public List<string> PublishedInfs = new List<string>();
    }

    internal class JobInfo
    {
        public int Id, Pages;
        public long Size;
        public string Document, Owner, Status, Submitted;
    }

    internal class DeviceInfo
    {
        public string Name, Status, Id, Class;
        public bool IsGenericUsb { get { return Name != null && Name.IndexOf("USB Printing Support", StringComparison.OrdinalIgnoreCase) >= 0; } }
    }

    internal class WuUpdate
    {
        public string Id, Title, Model, Date, Class, Manufacturer;
        public bool Relevant;
    }

    internal class ProcResult
    {
        public int ExitCode;
        public string Output = "";
    }

    /// <summary>All printer operations: WMI detection, PowerShell / pnputil driver management, spooler, backup, capture, reporting.</summary>
    internal static class PrinterService
    {
        public static Encoding Oem { get { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } }

        private static string Wql(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'"); }
        public static string Ps(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }
        private static string Str(ManagementBaseObject o, string p) { try { object v = o[p]; return v == null ? "" : v.ToString(); } catch { return ""; } }
        private static uint UInt(ManagementBaseObject o, string p) { uint v; return uint.TryParse(Str(o, p), out v) ? v : 0; }
        private static bool Bool(ManagementBaseObject o, string p) { return Str(o, p).Equals("True", StringComparison.OrdinalIgnoreCase); }

        public static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Printer";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = Regex.Replace(s, @"[\s]+", "_").Trim('_', '.');
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        // =====================================================================
        // Process / PowerShell runners
        // =====================================================================
        public static Task<ProcResult> Run(string file, string args, Action<string> onLine, Encoding enc)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                if (enc != null) { psi.StandardOutputEncoding = enc; psi.StandardErrorEncoding = enc; }
                var sb = new StringBuilder();
                using (var p = new Process { StartInfo = psi })
                {
                    DataReceivedEventHandler h = (s, e) =>
                    {
                        if (e.Data == null) return;
                        lock (sb) sb.AppendLine(e.Data);
                        if (onLine != null && e.Data.Trim().Length > 0) onLine("    " + e.Data.TrimEnd());
                    };
                    p.OutputDataReceived += h;
                    p.ErrorDataReceived += h;
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    p.WaitForExit(); // flush async readers
                    return new ProcResult { ExitCode = p.ExitCode, Output = sb.ToString() };
                }
            });
        }

        public static Task<ProcResult> RunPs(string script, Action<string> onLine)
        {
            string full = "$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + script;
            string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64, onLine, new UTF8Encoding(false));
        }

        // =====================================================================
        // Detection
        // =====================================================================
        public static List<PrinterInfo> GetPrinters()
        {
            var jobs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_PrintJob"))
                    foreach (ManagementObject j in s.Get())
                    {
                        string n = Str(j, "Name");
                        int i = n.LastIndexOf(',');
                        if (i <= 0) continue;
                        string pn = n.Substring(0, i);
                        int c; jobs.TryGetValue(pn, out c); jobs[pn] = c + 1;
                    }
            }
            catch { }

            var tcp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, HostAddress FROM Win32_TCPIPPrinterPort"))
                    foreach (ManagementObject o in s.Get()) tcp[Str(o, "Name")] = Str(o, "HostAddress");
            }
            catch { }

            var list = new List<PrinterInfo>();
            using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                foreach (ManagementObject o in s.Get())
                {
                    var p = new PrinterInfo
                    {
                        Name = Str(o, "Name"),
                        DriverName = Str(o, "DriverName"),
                        PortName = Str(o, "PortName"),
                        ShareName = Str(o, "ShareName"),
                        Location = Str(o, "Location"),
                        Comment = Str(o, "Comment"),
                        Status = UInt(o, "PrinterStatus"),
                        ErrorState = UInt(o, "DetectedErrorState"),
                        WorkOffline = Bool(o, "WorkOffline"),
                        IsDefault = Bool(o, "Default"),
                        IsNetwork = Bool(o, "Network"),
                        IsShared = Bool(o, "Shared")
                    };
                    int jc; if (jobs.TryGetValue(p.Name, out jc)) p.JobCount = jc;
                    string host;
                    if (!string.IsNullOrEmpty(p.PortName) && tcp.TryGetValue(p.PortName, out host)) p.HostAddress = host;
                    else
                    {
                        var m = Regex.Match(p.PortName ?? "", @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                        if (m.Success) p.HostAddress = m.Groups[1].Value;
                    }
                    p.Profile = ModelDatabase.Detect(p.Name, p.DriverName, null);
                    list.Add(p);
                }
            return list;
        }

        /// <summary>Scores printers so the most likely "real" device is auto-selected.</summary>
        public static int Score(PrinterInfo p)
        {
            int s = 0;
            if (p.Profile.IsVirtual) return -10;
            if (p.Profile.IsKnownBrand) s += 5;
            if (p.Profile.Language != CmdLang.Generic) s += 2;
            if (p.IsUsb) s += 3;
            if (p.IsDefault) s += 2;
            if (!p.WorkOffline) s += 1;
            return s;
        }

        public static string StatusText(PrinterInfo p, out HealthState state)
        {
            state = HealthState.Pass;
            if (p == null) { state = HealthState.Info; return "Unknown"; }
            if (p.WorkOffline) { state = HealthState.Fail; return "Offline"; }
            switch (p.ErrorState)
            {
                case 3: state = HealthState.Warn; return "Low Paper";
                case 4: state = HealthState.Fail; return "Out of Paper";
                case 5: state = HealthState.Warn; return "Low Toner/Ribbon";
                case 6: state = HealthState.Fail; return "No Toner/Ribbon";
                case 7: state = HealthState.Fail; return "Door / Head Open";
                case 8: state = HealthState.Fail; return "Paper Jam";
                case 9: state = HealthState.Fail; return "Offline";
                case 10: state = HealthState.Fail; return "Service Requested";
                case 11: state = HealthState.Warn; return "Output Bin Full";
            }
            switch (p.Status)
            {
                case 3: return "Ready";
                case 4: return "Printing";
                case 5: state = HealthState.Info; return "Warming Up";
                case 6: state = HealthState.Fail; return "Stopped";
                case 7: state = HealthState.Fail; return "Offline";
                default: state = HealthState.Info; return "Idle / Unknown";
            }
        }

        public static DriverInfo GetDriver(string driverName)
        {
            var d = new DriverInfo { Name = driverName };
            if (string.IsNullOrEmpty(driverName)) return d;
            try
            {
                var scope = new ManagementScope(@"root\StandardCimv2");
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_PrinterDriver WHERE Name='" + Wql(driverName) + "'")))
                    foreach (ManagementObject o in s.Get())
                    {
                        d.Found = true;
                        d.Manufacturer = Str(o, "Manufacturer");
                        d.InfPath = Str(o, "InfPath");
                        d.Environment = Str(o, "PrinterEnvironment");
                        d.OemUrl = Str(o, "OEMUrl");
                        d.Provider = Str(o, "Provider");
                        ulong v;
                        if (ulong.TryParse(Str(o, "DriverVersion"), out v) && v > 0)
                            d.Version = string.Format("{0}.{1}.{2}.{3}", (v >> 48) & 0xFFFF, (v >> 32) & 0xFFFF, (v >> 16) & 0xFFFF, v & 0xFFFF);
                        string dt = Str(o, "DriverDate");
                        if (dt.Length >= 14) { try { d.DriverDate = ManagementDateTimeConverter.ToDateTime(dt).ToString("yyyy-MM-dd"); } catch { } }
                        break;
                    }
            }
            catch { }
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DriverPath, InfName FROM Win32_PrinterDriver"))
                    foreach (ManagementObject o in s.Get())
                    {
                        string n = Str(o, "Name");
                        if (!n.StartsWith(driverName + ",", StringComparison.OrdinalIgnoreCase)) continue;
                        d.Found = true;
                        d.DriverPath = Str(o, "DriverPath");
                        if (string.IsNullOrEmpty(d.InfPath)) d.InfPath = Str(o, "InfName");
                        break;
                    }
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(d.DriverPath) && File.Exists(d.DriverPath))
                {
                    var fv = FileVersionInfo.GetVersionInfo(d.DriverPath);
                    d.FileVersion = fv.FileVersion;
                    if (string.IsNullOrEmpty(d.Provider)) d.Provider = fv.CompanyName;
                }
            }
            catch { }
            d.PublishedInfs = FindOemInfs(d.InfPath);
            return d;
        }

        /// <summary>Finds the published oemNN.inf for a Driver Store INF by binary comparison (language independent).</summary>
        public static List<string> FindOemInfs(string infPath)
        {
            var res = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(infPath) || !File.Exists(infPath)) return res;
                string infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
                string fname = Path.GetFileName(infPath);
                if (fname.StartsWith("oem", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetDirectoryName(infPath).TrimEnd('\\'), infDir, StringComparison.OrdinalIgnoreCase))
                { res.Add(fname); return res; }
                byte[] target = File.ReadAllBytes(infPath);
                foreach (var f in Directory.GetFiles(infDir, "oem*.inf"))
                {
                    if (new FileInfo(f).Length != target.Length) continue;
                    if (File.ReadAllBytes(f).SequenceEqual(target)) res.Add(Path.GetFileName(f));
                }
            }
            catch { }
            return res;
        }

        public static List<DeviceInfo> GetDevices(PrinterInfo p)
        {
            var res = new List<DeviceInfo>();
            var keys = new List<string>();
            if (p != null)
            {
                keys.AddRange(p.Profile.Keywords.Where(k => !string.IsNullOrEmpty(k) && k.Length > 1));
                if (!string.IsNullOrEmpty(p.DriverName)) keys.Add(p.DriverName);
            }
            Regex rx = keys.Count > 0 ? new Regex(string.Join("|", keys.Select(Regex.Escape)), RegexOptions.IgnoreCase) : null;

            ManagementObjectCollection items = null;
            try
            {
                items = new ManagementObjectSearcher("SELECT Name, Status, PNPDeviceID, PNPClass FROM Win32_PnPEntity WHERE PNPClass='Printer' OR PNPClass='USB' OR PNPClass='Image'").Get();
                items.Count.ToString();
            }
            catch
            {
                items = new ManagementObjectSearcher("SELECT Name, Status, PNPDeviceID FROM Win32_PnPEntity").Get();
            }
            foreach (ManagementObject o in items)
            {
                string name = Str(o, "Name");
                bool generic = name.IndexOf("USB Printing Support", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!generic && (rx == null || !rx.IsMatch(name))) continue;
                res.Add(new DeviceInfo { Name = name, Status = Str(o, "Status"), Id = Str(o, "PNPDeviceID"), Class = Str(o, "PNPClass") });
            }
            return res;
        }

        public static string Connection(PrinterInfo p, List<DeviceInfo> devs, out HealthState st, out string detail)
        {
            st = HealthState.Info; detail = "";
            if (p == null) return "Unknown";
            string port = p.PortName ?? "";
            if (!string.IsNullOrEmpty(p.HostAddress))
            {
                try
                {
                    using (var ping = new Ping())
                    {
                        var r = ping.Send(p.HostAddress, 1500);
                        if (r != null && r.Status == IPStatus.Success)
                        { st = HealthState.Pass; detail = p.HostAddress + " replied in " + r.RoundtripTime + " ms"; return "Network Online"; }
                    }
                }
                catch { }
                st = HealthState.Fail; detail = p.HostAddress + " is not responding to ping"; return "Network Unreachable";
            }
            if (p.IsUsb)
            {
                var real = devs.Where(d => !d.IsGenericUsb).ToList();
                if (real.Count > 0) { st = HealthState.Pass; detail = real[0].Name + " on " + port; return "USB Connected"; }
                if (devs.Any(d => d.IsGenericUsb)) { st = HealthState.Warn; detail = "A USB printer is present but was not identified as this model"; return "USB Device Present"; }
                st = HealthState.Fail; detail = "No matching USB hardware detected – check cable / power"; return "Disconnected";
            }
            if (Regex.IsMatch(port, "PORTPROMPT|FILE:|nul|PDF|XPS|SHRFAX|OneNote", RegexOptions.IgnoreCase)) { detail = "Software port " + port; return "Virtual Port"; }
            if (port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase)) { detail = "Web Services for Devices port"; return "WSD Network"; }
            if (p.IsNetwork) { detail = "Shared network queue"; return "Network Share"; }
            if (Regex.IsMatch(port, "^(COM|LPT)", RegexOptions.IgnoreCase)) { detail = "Serial/parallel port " + port; return "Local " + port.TrimEnd(':'); }
            detail = port;
            return "Local Port";
        }

        // =====================================================================
        // Spooler & queue
        // =====================================================================
        public static string SpoolerState()
        {
            try { using (var sc = new ServiceController("Spooler")) return sc.Status.ToString(); }
            catch { return "Unknown"; }
        }

        public static void StopSpooler(Action<string> log)
        {
            using (var sc = new ServiceController("Spooler"))
            {
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                log("Stopping Print Spooler service...");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }

        public static void StartSpooler(Action<string> log)
        {
            using (var sc = new ServiceController("Spooler"))
            {
                if (sc.Status == ServiceControllerStatus.Running) return;
                log("Starting Print Spooler service...");
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
        }

        public static int DeleteSpoolFiles(Action<string> log)
        {
            string dir = Path.Combine(Environment.SystemDirectory, @"spool\PRINTERS");
            int n = 0;
            if (!Directory.Exists(dir)) return 0;
            foreach (var f in Directory.GetFiles(dir))
            {
                try { File.Delete(f); n++; } catch (Exception ex) { log("  Could not delete " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            return n;
        }

        public static List<JobInfo> GetJobs(string printer)
        {
            var list = new List<JobInfo>();
            using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob"))
                foreach (ManagementObject o in s.Get())
                {
                    string n = Str(o, "Name");
                    if (printer != null && !n.StartsWith(printer + ",", StringComparison.OrdinalIgnoreCase)) continue;
                    var j = new JobInfo
                    {
                        Id = (int)UInt(o, "JobId"),
                        Document = Str(o, "Document"),
                        Owner = Str(o, "Owner"),
                        Status = Str(o, "JobStatus"),
                        Pages = (int)UInt(o, "TotalPages")
                    };
                    long sz; long.TryParse(Str(o, "Size"), out sz); j.Size = sz;
                    string ts = Str(o, "TimeSubmitted");
                    try { j.Submitted = ManagementDateTimeConverter.ToDateTime(ts).ToString("yyyy-MM-dd HH:mm:ss"); } catch { j.Submitted = ts; }
                    if (string.IsNullOrEmpty(j.Status)) j.Status = "Queued";
                    list.Add(j);
                }
            return list;
        }

        public static int CancelJobs(string printer)
        {
            int n = 0;
            using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob"))
                foreach (ManagementObject o in s.Get())
                {
                    if (!Str(o, "Name").StartsWith(printer + ",", StringComparison.OrdinalIgnoreCase)) continue;
                    try { o.Delete(); n++; } catch { }
                }
            return n;
        }

        private static ManagementObject PrinterObj(string name)
        {
            using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_Printer WHERE Name='" + Wql(name) + "'"))
                foreach (ManagementObject o in s.Get()) return o;
            throw new InvalidOperationException("Printer '" + name + "' not found.");
        }

        /// <summary>Invokes a Win32_Printer method (Pause, Resume, PrintTestPage, SetDefaultPrinter, CancelAllJobs).</summary>
        public static uint InvokePrinter(string name, string method)
        {
            var o = PrinterObj(name);
            object r = o.InvokeMethod(method, null);
            uint code; uint.TryParse(Convert.ToString(r), out code);
            return code;
        }

        public static void SetOnline(string name)
        {
            var o = PrinterObj(name);
            o["WorkOffline"] = false;
            o.Put();
        }

        // =====================================================================
        // Drivers
        // =====================================================================
        public static async Task<List<WuUpdate>> SearchWindowsUpdate(PrinterProfile prof, Action<string> log)
        {
            string script =
                "$ErrorActionPreference='Stop'; try { " +
                "$session = New-Object -ComObject Microsoft.Update.Session; " +
                "$searcher = $session.CreateUpdateSearcher(); " +
                "'Contacting Windows Update for driver updates (this can take 1-3 minutes)...'; " +
                "$res = $searcher.Search(\"IsInstalled=0 and Type='Driver' and IsHidden=0\"); " +
                "'Windows Update returned ' + $res.Updates.Count + ' driver update(s) for this PC.'; " +
                "for ($i=0; $i -lt $res.Updates.Count; $i++) { $u=$res.Updates.Item($i); " +
                "'UPD|' + $u.Identity.UpdateID + '|' + $u.Title + '|' + $u.DriverModel + '|' + $u.DriverVerDate + '|' + $u.DriverClass + '|' + $u.DriverManufacturer } " +
                "} catch { 'Windows Update search failed: ' + $_.Exception.Message }";
            var r = await RunPs(script, l => { if (!l.TrimStart().StartsWith("UPD|")) log(l); });
            var list = new List<WuUpdate>();
            string token = prof != null ? (prof.Keywords.LastOrDefault() ?? "") : "";
            foreach (var raw in r.Output.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("UPD|")) continue;
                var f = line.Split('|');
                if (f.Length < 7) continue;
                var u = new WuUpdate { Id = f[1], Title = f[2], Model = f[3], Date = f[4], Class = f[5], Manufacturer = f[6] };
                DateTime dt; if (DateTime.TryParse(u.Date, out dt)) u.Date = dt.ToString("yyyy-MM-dd");
                string all = (u.Title + " " + u.Model + " " + u.Manufacturer).ToLowerInvariant();
                u.Relevant = u.Class.IndexOf("print", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (prof != null && prof.IsKnownBrand && all.Contains(prof.Brand.ToLowerInvariant())) ||
                             (token.Length > 2 && all.Contains(token.ToLowerInvariant()));
                list.Add(u);
            }
            return list.OrderByDescending(x => x.Relevant).ToList();
        }

        public static Task<ProcResult> InstallWindowsUpdate(string updateId, Action<string> log)
        {
            string script =
                "$ErrorActionPreference='Stop'; try { " +
                "$session = New-Object -ComObject Microsoft.Update.Session; $searcher = $session.CreateUpdateSearcher(); " +
                "$res = $searcher.Search(\"UpdateID='" + updateId.Replace("'", "") + "'\"); " +
                "if ($res.Updates.Count -eq 0) { 'Update not found any more.'; return } " +
                "$coll = New-Object -ComObject Microsoft.Update.UpdateColl; " +
                "foreach ($u in $res.Updates) { if (-not $u.EulaAccepted) { $u.AcceptEula() }; [void]$coll.Add($u); 'Selected: ' + $u.Title } " +
                "'Downloading driver from Windows Update...'; $dl = $session.CreateUpdateDownloader(); $dl.Updates = $coll; $d = $dl.Download(); 'Download result code: ' + $d.ResultCode + ' (2 = succeeded)'; " +
                "'Installing driver...'; $ins = $session.CreateUpdateInstaller(); $ins.Updates = $coll; $r = $ins.Install(); " +
                "'Install result code: ' + $r.ResultCode + ' (2 = succeeded)  Reboot required: ' + $r.RebootRequired " +
                "} catch { 'Windows Update install failed: ' + $_.Exception.Message }";
            return RunPs(script, log);
        }

        public static async Task<string> Download(string url, string dir, Action<int> progress, Action<string> log)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288; }
            catch { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            Directory.CreateDirectory(dir);
            var uri = new Uri(url);
            string name = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(name) || !name.Contains(".")) name = "driver_download_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bin";
            string tmp = Path.Combine(dir, Safe(name) + ".part");
            string contentType = "";
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PrinterSupportToolkit/1.0");
                wc.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
                await wc.DownloadFileTaskAsync(uri, tmp);
                if (wc.ResponseHeaders != null)
                {
                    string cd = wc.ResponseHeaders["Content-Disposition"];
                    contentType = wc.ResponseHeaders["Content-Type"] ?? "";
                    if (!string.IsNullOrEmpty(cd))
                    {
                        var m = Regex.Match(cd, "filename\\*?=(?:UTF-8'')?\"?([^\";]+)\"?", RegexOptions.IgnoreCase);
                        if (m.Success) name = Uri.UnescapeDataString(m.Groups[1].Value.Trim());
                    }
                }
            }
            string final = Path.Combine(dir, Safe(name));
            if (File.Exists(final)) File.Delete(final);
            File.Move(tmp, final);
            if (contentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0)
                log("  ! The server returned a web page, not a driver file. Use a direct download link (vendor pages often need you to accept a EULA first).");
            return final;
        }

        private static bool IsPrinterInf(string f)
        {
            try { return Regex.IsMatch(File.ReadAllText(f), @"Class\s*=\s*Printer|4D36E979-E325-11CE-BFC1-08002BE10318", RegexOptions.IgnoreCase); }
            catch { return false; }
        }

        public static async Task<bool> InstallPackage(string path, string preferredDriver, PrinterProfile prof, Action<string> log)
        {
            if (Directory.Exists(path)) return await InstallFromFolder(path, preferredDriver, prof, log);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".zip")
            {
                string dest = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_extracted");
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                log("Extracting " + Path.GetFileName(path) + "...");
                await Task.Run(() => ZipFile.ExtractToDirectory(path, dest));
                return await InstallFromFolder(dest, preferredDriver, prof, log);
            }
            if (ext == ".inf") return await InstallInf(path, preferredDriver, prof, log);
            if (ext == ".exe" || ext == ".msi")
            {
                log("Launching installer: " + Path.GetFileName(path));
                log("  -> Complete the vendor setup wizard. The toolkit continues when the installer closes.");
                int code = await Task.Run(() =>
                {
                    var psi = ext == ".msi"
                        ? new ProcessStartInfo("msiexec.exe", "/i \"" + path + "\"") { UseShellExecute = true }
                        : new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) };
                    using (var pr = Process.Start(psi)) { pr.WaitForExit(); return pr.ExitCode; }
                });
                log("Installer finished with exit code " + code + ".");
                return code == 0 || code == 3010;
            }
            log("Unsupported file type: " + ext + " (use .exe, .msi, .inf, .zip or a folder).");
            return false;
        }

        private static async Task<bool> InstallFromFolder(string dir, string preferredDriver, PrinterProfile prof, Action<string> log)
        {
            var infs = Directory.GetFiles(dir, "*.inf", SearchOption.AllDirectories).Where(IsPrinterInf).ToList();
            if (infs.Count > 0)
            {
                string arch = Environment.Is64BitOperatingSystem ? "(amd64|x64|64)" : "(x86|i386|32)";
                string best = infs.FirstOrDefault(f => Regex.IsMatch(f, arch, RegexOptions.IgnoreCase)) ?? infs[0];
                log("Found printer INF: " + best.Substring(dir.Length).TrimStart('\\'));
                return await InstallInf(best, preferredDriver, prof, log);
            }
            var exe = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories)
                .OrderBy(f => Regex.IsMatch(Path.GetFileName(f), "setup|install|prndrv|driver", RegexOptions.IgnoreCase) ? 0 : 1)
                .FirstOrDefault();
            if (exe != null) return await InstallPackage(exe, preferredDriver, prof, log);
            log("No printer INF or installer found in " + dir);
            return false;
        }

        public static List<string> DriverNamesFromInf(string inf)
        {
            var names = new List<string>();
            try
            {
                var lines = File.ReadAllLines(inf);
                var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string section = "";
                foreach (var raw in lines)
                {
                    string l = raw.Trim();
                    if (l.StartsWith("[")) { section = l.Trim('[', ']').ToLowerInvariant(); continue; }
                    if (section == "strings" || section.StartsWith("strings."))
                    {
                        var m = Regex.Match(l, "^([^=;]+?)\\s*=\\s*\"?([^\"]*)\"?");
                        if (m.Success && !strings.ContainsKey(m.Groups[1].Value)) strings[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
                    }
                }
                section = "";
                foreach (var raw in lines)
                {
                    string l = raw.Trim();
                    if (l.StartsWith("[")) { section = l.Trim('[', ']').ToLowerInvariant(); continue; }
                    if (section == "strings" || section == "version" || section.StartsWith("sourcedisks") || section == "manufacturer") continue;
                    var m = Regex.Match(l, "^(\"([^\"]+)\"|%([^%]+)%)\\s*=\\s*[^,;]+,");
                    if (!m.Success) continue;
                    string n = m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : null;
                    if (n == null) { string v; if (strings.TryGetValue(m.Groups[3].Value, out v)) n = v; }
                    if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
                }
            }
            catch { }
            return names;
        }

        public static async Task<bool> InstallInf(string inf, string preferredDriver, PrinterProfile prof, Action<string> log)
        {
            log("Adding driver package to the Windows Driver Store (pnputil)...");
            var r = await Run("pnputil.exe", "/add-driver \"" + inf + "\" /install", log, Oem);
            var names = DriverNamesFromInf(inf);
            string pick = null;
            if (!string.IsNullOrEmpty(preferredDriver) && names.Any(n => n.Equals(preferredDriver, StringComparison.OrdinalIgnoreCase))) pick = preferredDriver;
            if (pick == null && prof != null)
            {
                string token = prof.Keywords.LastOrDefault() ?? "";
                string dpi = string.IsNullOrEmpty(prof.Dpi) ? "" : prof.Dpi.Split(' ')[0];
                if (token.Length > 1)
                {
                    pick = names.FirstOrDefault(n => n.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 && (dpi == "" || n.Contains(dpi)) &&
                                                     (prof.Language != CmdLang.ZPL || n.IndexOf("ZPL", StringComparison.OrdinalIgnoreCase) >= 0))
                        ?? names.FirstOrDefault(n => n.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            if (pick == null && !string.IsNullOrEmpty(preferredDriver)) pick = preferredDriver;
            if (pick == null)
            {
                log("Driver package staged. Could not determine the exact model name inside the INF (" + names.Count + " models).");
                log("  -> Use 'Open Printers & Scanners' to add the printer and pick the model.");
                return r.ExitCode == 0;
            }
            log("Registering printer driver: " + pick);
            var a = await RunPs(
                "try { Add-PrinterDriver -Name " + Ps(pick) + " -ErrorAction Stop; 'Driver registered: ' + " + Ps(pick) + " } " +
                "catch { try { Add-PrinterDriver -Name " + Ps(pick) + " -InfPath " + Ps(inf) + " -ErrorAction Stop; 'Driver registered from INF: ' + " + Ps(pick) + " } " +
                "catch { 'Add-PrinterDriver failed: ' + $_.Exception.Message } }", log);
            return a.Output.Contains("Driver registered");
        }

        public static async Task RemoveDriver(string driverName, List<string> oemInfs, Action<string> log)
        {
            string script =
                "$drv = " + Ps(driverName) + "; " +
                "$q = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.DriverName -eq $drv }); " +
                "foreach ($p in $q) { 'Removing printer queue: ' + $p.Name; Get-PrintJob -PrinterName $p.Name -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue; Remove-Printer -Name $p.Name -ErrorAction Continue } " +
                "'Restarting Print Spooler to release driver files...'; Restart-Service Spooler -Force; Start-Sleep -Seconds 3; " +
                "try { Remove-PrinterDriver -Name $drv -RemoveFromDriverStore -ErrorAction Stop; 'Driver removed from spooler and Driver Store.' } " +
                "catch { 'Remove-PrinterDriver (store): ' + $_.Exception.Message; try { Remove-PrinterDriver -Name $drv -ErrorAction Stop; 'Driver removed from spooler.' } catch { 'Remove-PrinterDriver: ' + $_.Exception.Message } }";
            await RunPs(script, log);
            foreach (var oem in oemInfs)
            {
                log("Deleting driver package " + oem + " from the Driver Store...");
                await Run("pnputil.exe", "/delete-driver " + oem + " /uninstall /force", log, Oem);
            }
        }

        public static Task<ProcResult> AddPrinter(string name, string driver, string port, Action<string> log)
        {
            var ip = Regex.Match(port ?? "", @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
            string script =
                "$port = " + Ps(port) + "; " +
                "if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { " +
                (ip.Success
                    ? "Add-PrinterPort -Name $port -PrinterHostAddress " + Ps(ip.Groups[1].Value) + " -ErrorAction Continue; 'Created TCP/IP port ' + $port; "
                    : "'Port ' + $port + ' is not present – plug in / power on the printer, then retry.'; ") +
                "} " +
                "try { Add-Printer -Name " + Ps(name) + " -DriverName " + Ps(driver) + " -PortName $port -ErrorAction Stop; 'Printer queue created: ' + " + Ps(name) + " } " +
                "catch { 'Add-Printer failed: ' + $_.Exception.Message }";
            return RunPs(script, log);
        }

        // =====================================================================
        // Backup & restore
        // =====================================================================
        public static async Task<string> Backup(PrinterInfo p, DriverInfo d, string root, bool driverPkg, bool full, Action<string> log)
        {
            string dir = Path.Combine(root, "Backups", Safe(p.Name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var ini = new StringBuilder();
            ini.AppendLine("[Backup]");
            ini.AppendLine("PrinterName=" + p.Name);
            ini.AppendLine("DriverName=" + p.DriverName);
            ini.AppendLine("PortName=" + p.PortName);
            ini.AppendLine("Brand=" + p.Profile.Brand);
            ini.AppendLine("Model=" + p.Profile.Model);
            ini.AppendLine("DriverVersion=" + (d != null ? d.Version : ""));
            ini.AppendLine("InfPath=" + (d != null ? d.InfPath : ""));
            ini.AppendLine("PublishedInf=" + (d != null ? string.Join(",", d.PublishedInfs) : ""));
            ini.AppendLine("Computer=" + Environment.MachineName);
            ini.AppendLine("Created=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            File.WriteAllText(Path.Combine(dir, "backup.ini"), ini.ToString());

            log("[1/5] Saving driver settings (DEVMODE, printer data) with PrintUI...");
            string dat = Path.Combine(dir, "printer_settings.dat");
            await Run("rundll32.exe", "printui.dll,PrintUIEntry /Ss /n \"" + p.Name + "\" /a \"" + dat + "\"", log, null);
            for (int i = 0; i < 20 && !File.Exists(dat); i++) await Task.Delay(250);
            log(File.Exists(dat) ? "  OK  printer_settings.dat" : "  !   PrintUI did not produce a settings file");

            log("[2/5] Exporting printer registry keys...");
            if (!p.Name.StartsWith("\\\\"))
                await Run("reg.exe", "export \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Print\\Printers\\" + p.Name + "\" \"" + Path.Combine(dir, "printer_registry.reg") + "\" /y", log, Oem);
            await Run("reg.exe", "export \"HKCU\\Printers\\DevModePerUser\" \"" + Path.Combine(dir, "user_devmodes.reg") + "\" /y", null, Oem);

            log("[3/5] Writing configuration report...");
            var r = await RunPs(
                "'=== Printer ==='; Get-Printer -Name " + Ps(p.Name) + " | Format-List * | Out-String -Width 220; " +
                "'=== Print Configuration ==='; Get-PrintConfiguration -PrinterName " + Ps(p.Name) + " -ErrorAction SilentlyContinue | Select-Object -Property Collate, Color, DuplexingMode, PaperSize, PaperWidth, PaperLength | Format-List | Out-String -Width 220; " +
                "'=== Printer Properties ==='; Get-PrinterProperty -PrinterName " + Ps(p.Name) + " -ErrorAction SilentlyContinue | Format-Table PropertyName,Type,Value -AutoSize | Out-String -Width 220; " +
                "'=== Driver ==='; Get-PrinterDriver -Name " + Ps(p.DriverName) + " | Format-List * | Out-String -Width 220", null);
            File.WriteAllText(Path.Combine(dir, "printer_configuration.txt"), SanitizeReportText(r.Output));
            log("  OK  printer_configuration.txt");

            log("[4/5] Exporting driver package...");
            if (driverPkg && d != null && d.PublishedInfs.Count > 0)
            {
                string pkg = Path.Combine(dir, "DriverPackage");
                Directory.CreateDirectory(pkg);
                foreach (var oem in d.PublishedInfs)
                    await Run("pnputil.exe", "/export-driver " + oem + " \"" + pkg + "\"", log, Oem);
            }
            else if (driverPkg) log("  -   Inbox Windows driver (no OEM package) – skipped.");
            else log("  -   Skipped by user.");

            log("[5/5] Full print-server backup (PrintBrm)...");
            string brm = Path.Combine(Environment.SystemDirectory, @"spool\tools\PrintBrm.exe");
            if (full && File.Exists(brm))
                await Run(brm, "-B -F \"" + Path.Combine(dir, "FullPrintBackup.printerExport") + "\" -O FORCE", log, Oem);
            else log(full ? "  -   PrintBrm.exe not available on this edition of Windows." : "  -   Skipped (enable 'Full print-server backup').");

            string zip = dir + ".zip";
            try { if (File.Exists(zip)) File.Delete(zip); await Task.Run(() => ZipFile.CreateFromDirectory(dir, zip)); log("  OK  Portable archive: " + Path.GetFileName(zip)); }
            catch (Exception ex) { log("  !   Zip failed: " + ex.Message); }
            return dir;
        }

        public static Dictionary<string, string> ReadIni(string file)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file)) return d;
            foreach (var l in File.ReadAllLines(file))
            {
                int i = l.IndexOf('=');
                if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
            return d;
        }

        private static string Get(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : ""; }

        public static async Task Restore(string dir, string targetPrinter, bool full, PrinterProfile prof, Action<string> log)
        {
            if (File.Exists(dir) && dir.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string dest = dir.Substring(0, dir.Length - 4) + "_restore";
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                await Task.Run(() => ZipFile.ExtractToDirectory(dir, dest));
                dir = dest;
            }
            var ini = ReadIni(Path.Combine(dir, "backup.ini"));
            string name = string.IsNullOrEmpty(targetPrinter) ? Get(ini, "PrinterName") : targetPrinter;
            string drv = Get(ini, "DriverName");
            string port = Get(ini, "PortName");
            log("Restoring backup to printer: " + name);

            bool exists = GetPrinters().Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                log("Printer queue does not exist – recreating it.");
                if (!GetDriver(drv).Found)
                {
                    string pkg = Path.Combine(dir, "DriverPackage");
                    if (Directory.Exists(pkg)) await InstallPackage(pkg, drv, prof, log);
                    else log("  ! Driver '" + drv + "' is not installed and the backup has no driver package.");
                }
                await AddPrinter(name, drv, port, log);
            }

            string dat = Path.Combine(dir, "printer_settings.dat");
            if (File.Exists(dat))
            {
                log("Restoring driver settings (global/user DEVMODE + printer data)...");
                await Run("rundll32.exe", "printui.dll,PrintUIEntry /Sr /n \"" + name + "\" /a \"" + dat + "\" d g u", log, null);
                log("  OK  Settings restored.");
            }
            else log("  ! printer_settings.dat not found in backup.");

            string brmFile = Path.Combine(dir, "FullPrintBackup.printerExport");
            string brm = Path.Combine(Environment.SystemDirectory, @"spool\tools\PrintBrm.exe");
            if (full && File.Exists(brmFile) && File.Exists(brm))
            {
                log("Restoring full print-server backup (PrintBrm)...");
                await Run(brm, "-R -F \"" + brmFile + "\" -O FORCE", log, Oem);
            }
        }

        // =====================================================================
        // Screenshots
        // =====================================================================
        /// <summary>Opens the Properties (/p) or Printing Preferences (/e) dialog and captures every tab as PNG.</summary>
        public static List<string> CaptureDialog(string printer, bool preferences, string outDir, Action<string> log)
        {
            var files = new List<string>();
            Directory.CreateDirectory(outDir);
            string prefix = preferences ? "Preferences" : "Properties";
            var before = new HashSet<IntPtr>(Native.TopWindows());
            uint myPid = (uint)Process.GetCurrentProcess().Id;
            var proc = Process.Start(new ProcessStartInfo("rundll32.exe", "printui.dll,PrintUIEntry " + (preferences ? "/e" : "/p") + " /n \"" + printer + "\"") { UseShellExecute = false });
            IntPtr dlg = IntPtr.Zero;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 25000 && dlg == IntPtr.Zero)
            {
                Thread.Sleep(400);
                foreach (var h in Native.TopWindows())
                {
                    if (before.Contains(h) || !Native.IsWindowVisible(h)) continue;
                    uint pid; Native.GetWindowThreadProcessId(h, out pid);
                    if (pid == myPid) continue;
                    string title = Native.WindowText(h);
                    if (Native.ClassName(h) == "#32770" && (pid == (uint)proc.Id || title.IndexOf(printer, StringComparison.OrdinalIgnoreCase) >= 0))
                    { dlg = h; break; }
                }
                if (dlg == IntPtr.Zero && proc.HasExited && sw.ElapsedMilliseconds > 8000) break;
            }
            if (dlg == IntPtr.Zero) { log("  ! Could not find the " + prefix + " dialog window."); return files; }

            Thread.Sleep(1500);
            Native.ShowWindow(dlg, Native.SW_RESTORE);
            Native.SetForegroundWindow(dlg);
            Thread.Sleep(400);
            IntPtr tab = Native.FindChildByClass(dlg, "SysTabControl32");
            int count = tab != IntPtr.Zero ? (int)Native.SendMessage(tab, Native.TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero) : 0;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Check if dialog has a vertical sidebar with section navigation buttons (e.g. Zebra Printing Preferences)
            var sidebars = Native.FindSidebarButtons(dlg);
            if (sidebars.Count > 1)
            {
                log("  Found dialog '" + Native.WindowText(dlg) + "' with " + sidebars.Count + " navigation section(s).");
                for (int s = 0; s < sidebars.Count; s++)
                {
                    var btn = sidebars[s];
                    Native.SendMessage(btn.Handle, Native.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    Thread.Sleep(500);

                    string file = Path.Combine(outDir, prefix + "_" + (s + 1).ToString("00") + "_" + Safe(btn.Name) + "_" + stamp + ".png");
                    using (var bmp = Native.CaptureWindow(dlg)) bmp.Save(file, ImageFormat.Png);
                    files.Add(file);
                    log("  OK  " + Path.GetFileName(file));
                }
            }
            else
            {
                log("  Found dialog '" + Native.WindowText(dlg) + "' with " + Math.Max(1, count) + " tab(s).");
                for (int i = 0; i < Math.Max(1, count); i++)
                {
                    if (count > 0) { Native.SendMessage(dlg, Native.PSM_SETCURSEL, (IntPtr)i, IntPtr.Zero); Thread.Sleep(1000); }
                    string page = "";
                    IntPtr pageH = Native.SendMessage(dlg, Native.PSM_GETCURRENTPAGEHWND, IntPtr.Zero, IntPtr.Zero);
                    if (pageH != IntPtr.Zero) page = Native.WindowText(pageH);
                    if (string.IsNullOrEmpty(page)) page = "Tab" + (i + 1);

                    var innerSidebars = Native.FindSidebarButtons(dlg);
                    if (innerSidebars.Count > 1)
                    {
                        for (int s = 0; s < innerSidebars.Count; s++)
                        {
                            var btn = innerSidebars[s];
                            Native.SendMessage(btn.Handle, Native.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                            Thread.Sleep(500);
                            string file = Path.Combine(outDir, prefix + "_" + (i + 1).ToString("00") + "_" + Safe(page) + "_" + (s + 1).ToString("00") + "_" + Safe(btn.Name) + "_" + stamp + ".png");
                            using (var bmp = Native.CaptureWindow(dlg)) bmp.Save(file, ImageFormat.Png);
                            files.Add(file);
                            log("  OK  " + Path.GetFileName(file));
                        }
                    }
                    else
                    {
                        string file = Path.Combine(outDir, prefix + "_" + (i + 1).ToString("00") + "_" + Safe(page) + "_" + stamp + ".png");
                        using (var bmp = Native.CaptureWindow(dlg)) bmp.Save(file, ImageFormat.Png);
                        files.Add(file);
                        log("  OK  " + Path.GetFileName(file));
                    }
                }
            }
            Native.PostMessage(dlg, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            try { if (!proc.WaitForExit(5000)) log("  ! Dialog still open – please close it manually."); } catch { }
            return files;
        }

        /// <summary>Renders a styled PNG "configuration card" of the printer, driver and print configuration.</summary>
        public static string RenderSnapshot(PrinterInfo p, DriverInfo d, List<KeyValuePair<string, string>> config, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var sections = new List<KeyValuePair<string, List<KeyValuePair<string, string>>>>();
            HealthState st;
            sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>("Printer", new List<KeyValuePair<string, string>>
            {
                KV("Queue name", p.Name), KV("Model", p.Profile.DisplayName), KV("Category", p.Profile.Category),
                KV("Command language", p.Profile.LanguageName), KV("Resolution", p.Profile.Dpi), KV("Port", p.PortName),
                KV("Status", StatusText(p, out st)), KV("Default printer", p.IsDefault ? "Yes" : "No"),
                KV("Shared", p.IsShared ? "Yes (" + p.ShareName + ")" : "No"), KV("Jobs in queue", p.JobCount.ToString())
            }));
            if (d != null)
                sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>("Driver", new List<KeyValuePair<string, string>>
                {
                    KV("Driver name", d.Name), KV("Version", d.Version), KV("File version", d.FileVersion), KV("Driver date", d.DriverDate),
                    KV("Manufacturer", d.Manufacturer), KV("INF", d.InfPath), KV("Published INF", string.Join(", ", d.PublishedInfs))
                }));
            if (config != null && config.Count > 0)
                sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>("Print configuration & driver properties", config));

            int rows = sections.Sum(s => s.Value.Count);
            int w = 1200, h = 170 + sections.Count * 60 + rows * 32 + 40;
            string file = Path.Combine(outDir, "ConfigSnapshot_" + Safe(p.Profile.Model) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            using (var bmp = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Theme.Bg);
                var head = new Rectangle(0, 0, w, 140);
                using (var br = new LinearGradientBrush(head, Color.FromArgb(22, 44, 70), Color.FromArgb(46, 28, 84), 0f)) g.FillRectangle(br, head);
                using (var f1 = new Font("Segoe UI Semibold", 11f)) using (var b1 = new SolidBrush(Theme.Accent)) g.DrawString("PRINTER CONFIGURATION SNAPSHOT", f1, b1, 36, 22);
                using (var f2 = new Font("Segoe UI", 26f, FontStyle.Bold)) g.DrawString(p.Profile.DisplayName, f2, Brushes.White, 32, 46);
                using (var f3 = new Font("Segoe UI", 10f)) using (var b3 = new SolidBrush(Theme.Muted))
                    g.DrawString(Environment.MachineName + "  •  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  •  Printer Support Toolkit", f3, b3, 38, 102);
                int y = 160;
                using (var fs = new Font("Segoe UI Semibold", 12f))
                using (var fk = new Font("Segoe UI", 10f))
                using (var fv = new Font("Segoe UI Semibold", 10f))
                using (var bk = new SolidBrush(Theme.Muted))
                using (var bv = new SolidBrush(Theme.Text))
                using (var ba = new SolidBrush(Theme.Accent))
                using (var bc = new SolidBrush(Theme.Card))
                using (var bc2 = new SolidBrush(Color.FromArgb(24, 31, 49)))
                {
                    foreach (var sec in sections)
                    {
                        g.FillRectangle(ba, 32, y + 8, 4, 22);
                        g.DrawString(sec.Key, fs, bv, 44, y + 5);
                        y += 44;
                        int i = 0;
                        foreach (var kv in sec.Value)
                        {
                            g.FillRectangle(i++ % 2 == 0 ? bc : bc2, 32, y, w - 64, 32);
                            g.DrawString(kv.Key, fk, bk, new RectangleF(46, y + 7, 330, 22));
                            g.DrawString(string.IsNullOrEmpty(kv.Value) ? "-" : kv.Value, fv, bv, new RectangleF(390, y + 7, w - 440, 22));
                            y += 32;
                        }
                        y += 16;
                    }
                }
                bmp.Save(file, ImageFormat.Png);
            }
            return file;
        }

        public static KeyValuePair<string, string> KV(string k, string v) { return new KeyValuePair<string, string>(k, v ?? ""); }

        public static async Task<List<KeyValuePair<string, string>>> GetPrintConfig(string printer)
        {
            var list = new List<KeyValuePair<string, string>>();
            var r = await RunPs(
                "$c = Get-PrintConfiguration -PrinterName " + Ps(printer) + " -ErrorAction SilentlyContinue; " +
                "if ($c) { 'Paper size=' + $c.PaperSize; 'Color=' + $c.Color; 'Collate=' + $c.Collate; 'Duplexing=' + $c.DuplexingMode } " +
                "Get-PrinterProperty -PrinterName " + Ps(printer) + " -ErrorAction SilentlyContinue | Select-Object -First 25 | ForEach-Object { $_.PropertyName + '=' + $_.Value }", null);
            foreach (var raw in r.Output.Split('\n'))
            {
                string l = raw.Trim();
                int i = l.IndexOf('=');
                if (i > 0) list.Add(KV(l.Substring(0, i).Replace("Config:", "").Replace("Property:", ""), l.Substring(i + 1)));
            }
            return list;
        }

        // =====================================================================
        // Diagnostic report
        // =====================================================================
        public static async Task<string> GenerateReport(PrinterInfo p, DriverInfo d, List<HealthItem> health, List<DeviceInfo> devs, string outDir, Action<string> log)
        {
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            string line = new string('=', 72);
            sb.AppendLine(line);
            sb.AppendLine((p.Profile.DisplayName + " DIAGNOSTIC REPORT").ToUpperInvariant());
            sb.AppendLine("Computer : " + Environment.MachineName);
            sb.AppendLine("User     : " + Environment.UserName);
            sb.AppendLine("Windows  : " + Environment.OSVersion.VersionString);
            sb.AppendLine("Date/Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("[0] DETECTED MODEL");
            sb.AppendLine("Brand        : " + p.Profile.BrandFull);
            sb.AppendLine("Model        : " + p.Profile.Model);
            sb.AppendLine("Category     : " + p.Profile.Category);
            sb.AppendLine("Language     : " + p.Profile.LanguageName);
            sb.AppendLine("Resolution   : " + (string.IsNullOrEmpty(p.Profile.Dpi) ? "-" : p.Profile.Dpi));
            sb.AppendLine("Queue        : " + p.Name);
            sb.AppendLine("Driver       : " + p.DriverName + "  (version " + (d != null ? d.Version : "?") + ", file " + (d != null ? d.FileVersion : "?") + ")");
            sb.AppendLine();
            sb.AppendLine("[H] HEALTH CHECK SUMMARY");
            foreach (var h in health) sb.AppendLine(string.Format("  [{0,-4}] {1} – {2}", h.State.ToString().ToUpperInvariant(), h.Title, h.Detail));
            sb.AppendLine();

            log("Collecting printer, driver, port, queue and event-log data...");
            string script =
                "$n=" + Ps(p.Name) + "; $drv=" + Ps(p.DriverName) + "; " +
                "'[1] PRINTER CONNECTION & STATUS'; Get-Printer -Name $n | Format-List Name,DriverName,PortName,PrinterStatus,WorkOffline,JobCount,Shared,Type,DeviceType,Location | Out-String -Width 200; " +
                "'[2] INSTALLED DRIVER VERSION & DETAILS'; Get-PrinterDriver -Name $drv -ErrorAction SilentlyContinue | Format-List Name,Manufacturer,MajorVersion,DriverVersion,InfPath,PrinterEnvironment,DriverDate | Out-String -Width 200; " +
                "Get-CimInstance Win32_PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $_.Name -like ($drv + ',*') } | Format-List Name,Version,DriverPath,InfName,SupportedPlatform | Out-String -Width 200; " +
                "'[3] PRINTER PORTS'; Get-PrinterPort -ErrorAction SilentlyContinue | Format-Table Name,Description,PortMonitor -AutoSize | Out-String -Width 200; " +
                "'[4] PRINT CONFIGURATION'; Get-PrintConfiguration -PrinterName $n -ErrorAction SilentlyContinue | Select-Object -Property Collate, Color, DuplexingMode, PaperSize, PaperWidth, PaperLength | Format-List | Out-String -Width 200; " +
                "'[5] PRINT QUEUE JOBS'; $j = Get-PrintJob -PrinterName $n -ErrorAction SilentlyContinue; if ($j) { $j | Format-Table ID,DocumentName,UserName,JobStatus,SubmittedTime,Size -AutoSize | Out-String -Width 200 } else { 'No jobs in queue.'; '' }; " +
                "'[6] PRINT SPOOLER SERVICE'; Get-Service Spooler | Format-List Name,Status,StartType | Out-String; " +
                "'[7] RECENT PRINT SERVICE ERRORS / WARNINGS (7 days)'; try { Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-PrintService/Admin'; StartTime=(Get-Date).AddDays(-7); Level=1,2,3} -MaxEvents 15 -ErrorAction Stop | Format-Table TimeCreated,Id,LevelDisplayName,Message -Wrap | Out-String -Width 200 } catch { 'No recent errors or warnings.'; '' }";
            var r = await RunPs(script, null);
            string sanitized = SanitizeReportText(r.Output.Replace("\r\n\r\n\r\n", "\r\n\r\n"));
            sb.Append(sanitized);
            sb.AppendLine("[8] LIVE USB / PLUG-AND-PLAY HARDWARE");
            if (devs.Count == 0) sb.AppendLine("No matching printer hardware detected right now.");
            foreach (var dv in devs) sb.AppendLine(string.Format("  {0,-45} {1,-6} {2}", dv.Name, dv.Status, dv.Id));
            sb.AppendLine();
            sb.AppendLine("[9] OFFICIAL SUPPORT / DRIVER LINKS");
            sb.AppendLine("Support page : " + p.Profile.SupportUrl);
            sb.AppendLine("Driver search: " + p.Profile.DriverSearchUrl);
            if (d != null && !string.IsNullOrEmpty(d.OemUrl)) sb.AppendLine("Driver OEM URL: " + d.OemUrl);
            sb.AppendLine(line);

            string baseName = "Diagnostic_Report_" + Safe(p.Profile.Model) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string txt = Path.Combine(outDir, baseName + ".txt");
            string reportContent = SanitizeReportText(sb.ToString());
            File.WriteAllText(txt, reportContent, Encoding.UTF8);

            // HTML version for sharing with support teams
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><title>" + WebUtilityEncode(p.Profile.DisplayName) + " Diagnostic Report</title>");
            html.Append("<style>body{font-family:'Segoe UI',sans-serif;background:#0b0f19;color:#e5ebf5;margin:0;padding:32px}h1{margin:0;font-size:30px}.hero{background:linear-gradient(110deg,#162c46,#2e1c54);padding:28px;border-radius:16px;border:1px solid #22d3ee55}.muted{color:#8693af}.chip{display:inline-block;padding:4px 12px;border-radius:12px;background:#ffffff1a;margin-right:6px;font-size:13px}table{width:100%;border-collapse:collapse;margin-top:18px;background:#151b2b;border-radius:12px;overflow:hidden}td{padding:10px 14px;border-bottom:1px solid #28314a}.PASS{color:#34d399}.WARN{color:#fbbf24}.FAIL{color:#f87171}.INFO{color:#60a5fa}pre{background:#151b2b;padding:20px;border-radius:12px;border:1px solid #28314a;white-space:pre-wrap;font-family:Consolas,monospace;font-size:13px}</style></head><body>");
            html.Append("<div class='hero'><div class='muted' style='letter-spacing:2px;color:#22d3ee'>" + WebUtilityEncode(p.Profile.BrandFull.ToUpperInvariant()) + "</div><h1>" + WebUtilityEncode(p.Profile.Model) + "</h1><p><span class='chip'>" + WebUtilityEncode(p.Profile.Category) + "</span><span class='chip'>" + WebUtilityEncode(p.Profile.LanguageName) + "</span><span class='muted'>" + WebUtilityEncode(Environment.MachineName + " • " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")) + "</span></p></div>");
            html.Append("<h2>Health checks</h2><table>");
            foreach (var h in health)
                html.Append("<tr><td class='" + h.State.ToString().ToUpperInvariant() + "'><b>" + h.State.ToString().ToUpperInvariant() + "</b></td><td><b>" + WebUtilityEncode(h.Title) + "</b></td><td class='muted'>" + WebUtilityEncode(h.Detail) + "</td></tr>");
            html.Append("</table><h2>Full report</h2><pre>" + WebUtilityEncode(reportContent) + "</pre></body></html>");
            File.WriteAllText(Path.Combine(outDir, baseName + ".html"), html.ToString(), Encoding.UTF8);
            return txt;
        }

        public static string SanitizeReportText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                // Skip lines with long repeating characters (e.g., base64 DEVMODE padding of 15+ repeating A's or 0's)
                if (Regex.IsMatch(l, @"A{15,}|0{20,}")) continue;
                // Skip XML devmode snapshot / print ticket junk if present
                if (l.IndexOf("<psf:", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (l.IndexOf("xmlns:psf", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (l.IndexOf("PageDevmodeSnapshot", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                sb.AppendLine(l);
            }
            return sb.ToString();
        }

        private static string WebUtilityEncode(string s) { return WebUtility.HtmlEncode(s ?? ""); }
    }
}
