using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrinterToolkit
{
    internal class MainForm : Form
    {
        // Data state
        private List<PrinterInfo> allPrinters = new List<PrinterInfo>();
        private PrinterInfo curPrinter;
        private DriverInfo curDriver;
        private List<DeviceInfo> curDevices = new List<DeviceInfo>();
        private readonly List<HealthItem> curHealth = new List<HealthItem>();
        private string outputDir;

        // UI Layout
        private Panel sidebar;
        private Panel contentHost;
        private Panel topBar;
        private ComboBox cboPrinters;
        private ComboBox cboTargetPrinter;
        private Label lblSelectedModel;
        private ThinProgress progBar;
        private TextBox txtActivityLog;
        private FlowLayoutPanel pnlScreenshots;
        private readonly Dictionary<string, Panel> viewPanels = new Dictionary<string, Panel>();
        private readonly Dictionary<string, NavButton> navButtons = new Dictionary<string, NavButton>();
        private string activeView = "dashboard";

        // View controls
        private HeroCard heroCard;
        private StatTile tileDriver, tilePort, tileSpooler, tileJobs;
        private HealthList healthList;
        private KeyValueView kvGeneral;
        private DataGridView gridJobs;
        private DataGridView gridUpdates;
        private PulseIndicator pulseMonitor;
        private TextBox txtUsbLog;
        private FlowLayoutPanel pnlModelActions;
        private TextBox txtCustomCmd;
        private Timer usbPollTimer;
        private string lastUsbSignature = "";

        public MainForm()
        {
            InitializeComponent();
            Native.EnableDarkTitleBar(Handle);
            outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Printer_Diagnostics");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
        }

        private void InitializeComponent()
        {
            Text = "Printer Diagnostic & Repair Toolkit - Advanced Edition";
            Size = new Size(1220, 820);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F9;

            // Top Bar
            topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Theme.Sidebar,
                Padding = new Padding(0)
            };

            var pnlBrand = new Panel
            {
                Dock = DockStyle.Left,
                Width = 240,
                BackColor = Theme.Sidebar,
                Padding = new Padding(16, 11, 10, 10)
            };

            var lblApp = new Label
            {
                Text = "PRINTER TOOLKIT",
                Font = Theme.Semi11,
                ForeColor = Theme.Accent,
                Location = new Point(16, 11),
                AutoSize = true
            };

            lblSelectedModel = new Label
            {
                Text = "Diagnostic & Repair Suite",
                Font = Theme.Semi9,
                ForeColor = Theme.Muted,
                Location = new Point(16, 33),
                Size = new Size(210, 20),
                AutoEllipsis = true
            };

            pnlBrand.Controls.Add(lblApp);
            pnlBrand.Controls.Add(lblSelectedModel);

            var flpActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Sidebar,
                Padding = new Padding(6, 14, 16, 10)
            };

            var lblSelect = new Label
            {
                Text = "Target Printer:",
                Font = Theme.Semi9,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 8, 8, 0)
            };

            cboPrinters = new ComboBox
            {
                Width = 320,
                DropDownWidth = 460,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.CardAlt,
                ForeColor = Theme.Text,
                Font = Theme.Semi9,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 4, 12, 0)
            };
            cboPrinters.SelectedIndexChanged += (s, e) => OnPrinterSelectionChanged();

            var btnRefreshPrinters = new ModernButton("Refresh", Glyph.Refresh, ButtonKind.Secondary)
            {
                Size = new Size(110, 36),
                Margin = new Padding(0, 0, 8, 0)
            };
            btnRefreshPrinters.Click += async (s, e) => await RefreshPrintersListAsync();

            var btnOpenWinSettings = new ModernButton("Windows Printers", Glyph.Settings, ButtonKind.Secondary)
            {
                Size = new Size(165, 36),
                Margin = new Padding(0, 0, 8, 0)
            };
            btnOpenWinSettings.Click += (s, e) => Process.Start("control", "printers");

            var btnOpenOutDir = new ModernButton("Open Folder", Glyph.Folder, ButtonKind.Secondary)
            {
                Size = new Size(140, 36),
                Margin = new Padding(0, 0, 8, 0)
            };
            btnOpenOutDir.Click += (s, e) => Process.Start(new ProcessStartInfo(outputDir) { UseShellExecute = true });

            flpActions.Controls.AddRange(new Control[] {
                lblSelect, cboPrinters, btnRefreshPrinters, btnOpenWinSettings, btnOpenOutDir
            });

            topBar.Controls.Add(flpActions);
            topBar.Controls.Add(pnlBrand);

            // Progress bar directly below top bar
            progBar = new ThinProgress { Dock = DockStyle.Top };

            // Sidebar
            sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 240,
                BackColor = Theme.Sidebar,
                Padding = new Padding(0, 10, 0, 10)
            };

            AddNav("dashboard", "Overview & Health", Glyph.Home);
            AddNav("spooler", "Spooler & Queue", Glyph.Repair);
            AddNav("driver", "Driver Manager", Glyph.Download);
            AddNav("backup", "Backup & Restore", Glyph.Save);
            AddNav("screenshots", "Config Screenshots", Glyph.Camera);
            AddNav("usb", "Live USB Monitor", Glyph.Usb);
            AddNav("direct", "Model Direct Tools", Glyph.Bolt);
            AddNav("report", "Diagnostic Report", Glyph.Document);

            // Bottom activity / log drawer
            var bottomDrawer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 130,
                BackColor = Theme.CardAlt,
                Padding = new Padding(12, 6, 12, 8)
            };

            var lblLogHeader = new Label
            {
                Text = "ACTIVITY / OPERATION LOG",
                Font = Theme.Caps8,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 18
            };

            txtActivityLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono9
            };

            bottomDrawer.Controls.Add(txtActivityLog);
            bottomDrawer.Controls.Add(lblLogHeader);

            // Main Content Host
            contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Bg,
                Padding = new Padding(12)
            };

            BuildDashboardView();
            BuildSpoolerView();
            BuildDriverView();
            BuildBackupView();
            BuildScreenshotsView();
            BuildUsbView();
            BuildDirectToolsView();
            BuildReportView();

            Controls.Add(contentHost);
            Controls.Add(sidebar);
            Controls.Add(progBar);
            Controls.Add(topBar);
            Controls.Add(bottomDrawer);

            Shown += async (s, e) =>
            {
                SwitchView("dashboard");
                await RefreshPrintersListAsync();
            };
        }

        private void AddNav(string key, string text, string glyph)
        {
            var btn = new NavButton(key, text, glyph)
            {
                Dock = DockStyle.Top
            };
            btn.Click += (s, e) => SwitchView(key);
            sidebar.Controls.Add(btn);
            sidebar.Controls.SetChildIndex(btn, 0); // stack in insertion order
            navButtons[key] = btn;
        }

        private void SwitchView(string key)
        {
            activeView = key;
            foreach (var kv in navButtons)
            {
                kv.Value.Selected = (kv.Key == key);
            }
            foreach (var kv in viewPanels)
            {
                kv.Value.Visible = (kv.Key == key);
            }

            if (key == "usb")
            {
                StartUsbMonitor();
            }
            else
            {
                StopUsbMonitor();
            }
        }

        private void Log(string msg)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(Log), msg);
                return;
            }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine;
            txtActivityLog.AppendText(line);
        }

        // =====================================================================
        // VIEW BUILDERS
        // =====================================================================
        private void BuildDashboardView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };
            
            heroCard = new HeroCard { Height = 140, Dock = DockStyle.Top };

            var statsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 98,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0, 4, 0, 6)
            };
            statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            tileDriver = new StatTile("Driver Version", Glyph.Package, Theme.Accent);
            tilePort = new StatTile("Port & Conn", Glyph.Usb, Theme.Success);
            tileSpooler = new StatTile("Spooler Service", Glyph.Repair, Theme.Info);
            tileJobs = new StatTile("Stuck Queue Jobs", Glyph.Queue, Theme.Warning);

            statsLayout.Controls.Add(tileDriver, 0, 0);
            statsLayout.Controls.Add(tilePort, 1, 0);
            statsLayout.Controls.Add(tileSpooler, 2, 0);
            statsLayout.Controls.Add(tileJobs, 3, 0);

            var splitBottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            splitBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            splitBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            var cardHealth = new CardPanel("Diagnostic Health Checks", Glyph.Diagnostic);
            healthList = new HealthList();
            cardHealth.Controls.Add(healthList);

            var cardProps = new CardPanel("Hardware & Queue Properties", Glyph.Info);
            kvGeneral = new KeyValueView();
            cardProps.Controls.Add(kvGeneral);

            splitBottom.Controls.Add(cardHealth, 0, 0);
            splitBottom.Controls.Add(cardProps, 1, 0);

            pnl.Controls.Add(splitBottom);
            pnl.Controls.Add(statsLayout);
            pnl.Controls.Add(heroCard);

            contentHost.Controls.Add(pnl);
            viewPanels["dashboard"] = pnl;
        }

        private void BuildSpoolerView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };

            var actionsCard = new CardPanel("Spooler Maintenance & Queue Control", Glyph.Repair)
            {
                Dock = DockStyle.Top,
                Height = 130
            };

            var flp = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };

            var btnClearSpooler = new ModernButton("Clear Spooler (Fix Stuck)", Glyph.Repair, ButtonKind.Primary) { Width = 230 };
            btnClearSpooler.Click += async (s, e) =>
            {
                progBar.Running = true;
                Log("Starting Print Spooler deep clean...");
                await Task.Run(() =>
                {
                    try
                    {
                        PrinterService.StopSpooler(Log);
                        int deleted = PrinterService.DeleteSpoolFiles(Log);
                        Log("Deleted " + deleted + " stuck spool files.");
                        PrinterService.StartSpooler(Log);
                        Log("Print Spooler restarted successfully!");
                    }
                    catch (Exception ex)
                    {
                        Log("ERROR clearing spooler: " + ex.Message);
                    }
                });
                progBar.Running = false;
                await RefreshDiagnosticsAsync();
            };

            var btnRestartSpooler = new ModernButton("Restart Spooler", Glyph.Refresh, ButtonKind.Secondary) { Width = 170 };
            btnRestartSpooler.Click += async (s, e) =>
            {
                progBar.Running = true;
                await Task.Run(() =>
                {
                    try
                    {
                        PrinterService.StopSpooler(Log);
                        PrinterService.StartSpooler(Log);
                        Log("Spooler service restarted.");
                    }
                    catch (Exception ex) { Log("Spooler restart failed: " + ex.Message); }
                });
                progBar.Running = false;
                await RefreshDiagnosticsAsync();
            };

            var btnPurgeQueue = new ModernButton("Purge All Jobs In Queue", Glyph.Delete, ButtonKind.Danger) { Width = 210 };
            btnPurgeQueue.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                progBar.Running = true;
                await Task.Run(() =>
                {
                    int n = PrinterService.CancelJobs(curPrinter.Name);
                    Log("Cancelled " + n + " print jobs for " + curPrinter.Name);
                });
                progBar.Running = false;
                await RefreshDiagnosticsAsync();
            };

            var btnPrintTest = new ModernButton("Print Windows Test Page", Glyph.Printer, ButtonKind.Secondary) { Width = 210 };
            btnPrintTest.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                try
                {
                    uint code = PrinterService.InvokePrinter(curPrinter.Name, "PrintTestPage");
                    Log("Sent Windows Test Page to " + curPrinter.Name + " (Result: " + code + ")");
                }
                catch (Exception ex) { Log("Print test page failed: " + ex.Message); }
                await RefreshDiagnosticsAsync();
            };

            flp.Controls.AddRange(new Control[] { btnClearSpooler, btnRestartSpooler, btnPurgeQueue, btnPrintTest });
            actionsCard.Controls.Add(flp);

            var cardQueue = new CardPanel("Active Print Queue Jobs", Glyph.Queue)
            {
                Dock = DockStyle.Fill
            };

            gridJobs = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(gridJobs);
            gridJobs.Columns.Add("Id", "Job ID");
            gridJobs.Columns.Add("Doc", "Document Name");
            gridJobs.Columns.Add("Owner", "User");
            gridJobs.Columns.Add("Pages", "Pages");
            gridJobs.Columns.Add("Size", "Size");
            gridJobs.Columns.Add("Status", "Job Status");
            gridJobs.Columns.Add("Time", "Submitted Time");

            cardQueue.Controls.Add(gridJobs);

            pnl.Controls.Add(cardQueue);
            pnl.Controls.Add(actionsCard);

            contentHost.Controls.Add(pnl);
            viewPanels["spooler"] = pnl;
        }

        private void BuildDriverView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };

            var actionsCard = new CardPanel("Driver Management & Actions", Glyph.Download)
            {
                Dock = DockStyle.Top,
                Height = 150
            };

            var flp = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };

            var btnCheckWu = new ModernButton("Search Windows Update", Glyph.Search, ButtonKind.Primary) { Width = 220 };
            btnCheckWu.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                progBar.Running = true;
                Log("Searching Windows Update for printer driver updates...");
                try
                {
                    var updates = await PrinterService.SearchWindowsUpdate(curPrinter.Profile, Log);
                    gridUpdates.Rows.Clear();
                    foreach (var u in updates)
                    {
                        gridUpdates.Rows.Add(u.Title, u.Model, u.Date, u.Class, u.Id);
                    }
                    Log("Windows Update check complete. Found " + updates.Count + " drivers.");
                }
                catch (Exception ex) { Log("Windows update search error: " + ex.Message); }
                progBar.Running = false;
            };

            var btnInstallFromDisk = new ModernButton("Install Driver (.exe / .inf / .zip)", Glyph.Upload, ButtonKind.Secondary) { Width = 260 };
            btnInstallFromDisk.Click += async (s, e) =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select Driver Package (.exe, .msi, .inf, .zip)";
                    ofd.Filter = "Driver Packages (*.exe;*.msi;*.inf;*.zip)|*.exe;*.msi;*.inf;*.zip|All files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        progBar.Running = true;
                        Log("Installing package: " + ofd.FileName);
                        bool ok = await PrinterService.InstallPackage(ofd.FileName, curPrinter != null ? curPrinter.DriverName : null, curPrinter != null ? curPrinter.Profile : null, Log);
                        Log(ok ? "Driver installation succeeded." : "Driver installation finished with warnings.");
                        progBar.Running = false;
                        await RefreshDiagnosticsAsync();
                    }
                }
            };

            var btnOpenSupport = new ModernButton("Open Official Support Site", Glyph.Globe, ButtonKind.Secondary) { Width = 230 };
            btnOpenSupport.Click += (s, e) =>
            {
                if (curPrinter != null && !string.IsNullOrEmpty(curPrinter.Profile.SupportUrl))
                {
                    Process.Start(new ProcessStartInfo(curPrinter.Profile.SupportUrl) { UseShellExecute = true });
                }
            };

            var btnDeleteDriver = new ModernButton("Uninstall & Delete Driver", Glyph.Delete, ButtonKind.Danger) { Width = 230 };
            btnDeleteDriver.Click += async (s, e) =>
            {
                if (curDriver == null || !curDriver.Found)
                {
                    MessageBox.Show("No driver found to remove.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var res = MessageBox.Show(
                    "Are you sure you want to completely delete the driver:\n\n" + curDriver.Name +
                    "\n\nThis will remove queues using it and delete packages from the Windows Driver Store.",
                    "Confirm Driver Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (res != DialogResult.Yes) return;

                progBar.Running = true;
                Log("Starting driver uninstallation...");
                try
                {
                    await PrinterService.RemoveDriver(curDriver.Name, curDriver.PublishedInfs, Log);
                    Log("Driver removal process finished.");
                }
                catch (Exception ex)
                {
                    Log("Driver removal failed: " + ex.Message);
                }
                progBar.Running = false;
                await RefreshPrintersListAsync();
            };

            flp.Controls.AddRange(new Control[] { btnCheckWu, btnInstallFromDisk, btnOpenSupport, btnDeleteDriver });
            actionsCard.Controls.Add(flp);

            var cardWu = new CardPanel("Windows Update Driver Results", Glyph.Package)
            {
                Dock = DockStyle.Fill
            };

            gridUpdates = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(gridUpdates);
            gridUpdates.Columns.Add("Title", "Driver Title");
            gridUpdates.Columns.Add("Model", "Model");
            gridUpdates.Columns.Add("Date", "Driver Date");
            gridUpdates.Columns.Add("Class", "Class");
            gridUpdates.Columns.Add("Id", "Update ID");

            var btnInstallSelectedWu = new ModernButton("Install Selected Windows Update Driver", Glyph.Download, ButtonKind.Primary)
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };
            btnInstallSelectedWu.Click += async (s, e) =>
            {
                if (gridUpdates.SelectedRows.Count == 0) return;
                object cellVal = gridUpdates.SelectedRows[0].Cells["Id"].Value;
                string uid = cellVal != null ? cellVal.ToString() : null;
                if (string.IsNullOrEmpty(uid)) return;

                progBar.Running = true;
                Log("Triggering Windows Update install for " + uid + "...");
                var r = await PrinterService.InstallWindowsUpdate(uid, Log);
                Log(r.Output);
                progBar.Running = false;
                await RefreshDiagnosticsAsync();
            };

            cardWu.Controls.Add(gridUpdates);
            cardWu.Controls.Add(btnInstallSelectedWu);

            pnl.Controls.Add(cardWu);
            pnl.Controls.Add(actionsCard);

            contentHost.Controls.Add(pnl);
            viewPanels["driver"] = pnl;
        }

        private void BuildBackupView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };

            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            var cardBackup = new CardPanel("Backup Printer Configuration & Driver", Glyph.Save);
            var flpB = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };

            var chkIncludeDriver = new CheckBox
            {
                Text = "Include Driver Store INF package (pnputil export)",
                ForeColor = Theme.Text,
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };

            var chkFullBrm = new CheckBox
            {
                Text = "Full print server migration archive (PrintBrm.exe)",
                ForeColor = Theme.Text,
                Checked = false,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 20)
            };

            var btnDoBackup = new ModernButton("Create Full Backup Archive (.zip)", Glyph.Save, ButtonKind.Primary)
            {
                Width = 320,
                Height = 44
            };
            btnDoBackup.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                progBar.Running = true;
                Log("Starting backup for " + curPrinter.Name + "...");
                try
                {
                    string resDir = await PrinterService.Backup(curPrinter, curDriver, outputDir, chkIncludeDriver.Checked, chkFullBrm.Checked, Log);
                    Log("Backup completed: " + resDir);
                }
                catch (Exception ex)
                {
                    Log("Backup failed: " + ex.Message);
                }
                progBar.Running = false;
            };

            flpB.Controls.AddRange(new Control[] { chkIncludeDriver, chkFullBrm, btnDoBackup });
            cardBackup.Controls.Add(flpB);

            var cardRestore = new CardPanel("Restore Printer Configuration & Settings", Glyph.Restore);
            var flpR = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };

            var lblTarget = new Label
            {
                Text = "Restore into Printer Queue:",
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };

            cboTargetPrinter = new ComboBox
            {
                Width = 320,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.CardAlt,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 0, 20)
            };

            var btnDoRestore = new ModernButton("Select Backup Folder / ZIP to Restore", Glyph.Restore, ButtonKind.Secondary)
            {
                Width = 320,
                Height = 44
            };
            btnDoRestore.Click += async (s, e) =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select Backup .ZIP or backup.ini";
                    ofd.Filter = "Backup Files (*.zip;backup.ini)|*.zip;backup.ini|All files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        progBar.Running = true;
                        string chosen = ofd.FileName;
                        if (chosen.EndsWith("backup.ini", StringComparison.OrdinalIgnoreCase))
                            chosen = Path.GetDirectoryName(chosen);

                        string target = cboTargetPrinter.SelectedItem != null ? cboTargetPrinter.SelectedItem.ToString() : null;
                        Log("Restoring backup: " + chosen + " to queue: " + target);
                        try
                        {
                            await PrinterService.Restore(chosen, target, false, curPrinter != null ? curPrinter.Profile : null, Log);
                            Log("Restore completed successfully.");
                        }
                        catch (Exception ex) { Log("Restore error: " + ex.Message); }
                        progBar.Running = false;
                        await RefreshDiagnosticsAsync();
                    }
                }
            };

            flpR.Controls.AddRange(new Control[] { lblTarget, cboTargetPrinter, btnDoRestore });
            cardRestore.Controls.Add(flpR);

            split.Controls.Add(cardBackup, 0, 0);
            split.Controls.Add(cardRestore, 1, 0);

            pnl.Controls.Add(split);
            contentHost.Controls.Add(pnl);
            viewPanels["backup"] = pnl;
        }

        private void BuildScreenshotsView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };

            var topActions = new CardPanel("Automatic Printer Configuration Capture", Glyph.Camera)
            {
                Dock = DockStyle.Top,
                Height = 110
            };

            var flp = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };

            var btnCaptureProps = new ModernButton("Capture Properties Tabs", Glyph.Camera, ButtonKind.Primary) { Width = 230 };
            btnCaptureProps.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                progBar.Running = true;
                Log("Opening Printer Properties and capturing dialog tabs...");
                var files = await Task.Run(() => PrinterService.CaptureDialog(curPrinter.Name, false, Path.Combine(outputDir, "Screenshots"), Log));
                Log("Captured " + files.Count + " tab screenshots.");
                progBar.Running = false;
                LoadScreenshotsList();
            };

            var btnCapturePrefs = new ModernButton("Capture Preferences Sections", Glyph.Camera, ButtonKind.Secondary) { Width = 230 };
            btnCapturePrefs.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                progBar.Running = true;
                Log("Opening Printing Preferences and capturing all sections...");
                var files = await Task.Run(() => PrinterService.CaptureDialog(curPrinter.Name, true, Path.Combine(outputDir, "Screenshots"), Log));
                Log("Captured " + files.Count + " preference section screenshot(s).");
                progBar.Running = false;
                LoadScreenshotsList();
            };

            var btnRenderSummary = new ModernButton("Render Visual Config Card (PNG)", Glyph.Document, ButtonKind.Secondary) { Width = 260 };
            btnRenderSummary.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                progBar.Running = true;
                Log("Rendering styled configuration card snapshot...");
                var cfg = await PrinterService.GetPrintConfig(curPrinter.Name);
                string file = PrinterService.RenderSnapshot(curPrinter, curDriver, cfg, Path.Combine(outputDir, "Screenshots"));
                Log("Rendered snapshot: " + file);
                progBar.Running = false;
                LoadScreenshotsList();
                try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { }
            };

            flp.Controls.AddRange(new Control[] { btnCaptureProps, btnCapturePrefs, btnRenderSummary });
            topActions.Controls.Add(flp);

            var cardGallery = new CardPanel("Captured Screenshots & Visual Cards (Click to open)", Glyph.Camera)
            {
                Dock = DockStyle.Fill
            };

            pnlScreenshots = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Card,
                Padding = new Padding(8)
            };

            cardGallery.Controls.Add(pnlScreenshots);

            pnl.Controls.Add(cardGallery);
            pnl.Controls.Add(topActions);

            contentHost.Controls.Add(pnl);
            viewPanels["screenshots"] = pnl;
        }

        private void LoadScreenshotsList()
        {
            pnlScreenshots.Controls.Clear();
            string sdir = Path.Combine(outputDir, "Screenshots");
            if (!Directory.Exists(sdir)) return;

            var pngs = Directory.GetFiles(sdir, "*.png").OrderByDescending(f => File.GetCreationTime(f));
            foreach (var f in pngs)
            {
                pnlScreenshots.Controls.Add(new ThumbTile(f));
            }
        }

        private void BuildUsbView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };

            var topStatus = new CardPanel("Live Hardware Status", Glyph.Usb)
            {
                Dock = DockStyle.Top,
                Height = 150
            };

            pulseMonitor = new PulseIndicator();
            topStatus.Controls.Add(pulseMonitor);

            var bottomCard = new CardPanel("USB Reconnection Event Log", Glyph.Sync)
            {
                Dock = DockStyle.Fill
            };

            txtUsbLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono9
            };

            bottomCard.Controls.Add(txtUsbLog);

            pnl.Controls.Add(bottomCard);
            pnl.Controls.Add(topStatus);

            contentHost.Controls.Add(pnl);
            viewPanels["usb"] = pnl;

            usbPollTimer = new Timer { Interval = 1500 };
            usbPollTimer.Tick += async (s, e) => await PollUsbStateAsync();
        }

        private void StartUsbMonitor()
        {
            pulseMonitor.Animate = true;
            usbPollTimer.Start();
            txtUsbLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] Started live USB hardware polling (1.5s interval)...\r\n");
        }

        private void StopUsbMonitor()
        {
            pulseMonitor.Animate = false;
            usbPollTimer.Stop();
        }

        private async Task PollUsbStateAsync()
        {
            if (curPrinter == null) return;
            var devs = await Task.Run(() => PrinterService.GetDevices(curPrinter));
            HealthState st;
            string detail;
            string conn = PrinterService.Connection(curPrinter, devs, out st, out detail);

            pulseMonitor.StateColor = HealthList.ColorOf(st);
            pulseMonitor.Title = conn;
            pulseMonitor.Detail = detail;

            string sig = conn + "|" + detail + "|" + devs.Count;
            if (sig != lastUsbSignature)
            {
                lastUsbSignature = sig;
                string msg = string.Format("[{0}] [{1}] Status: {2} | Hardware: {3}\r\n",
                    DateTime.Now.ToString("HH:mm:ss"),
                    st == HealthState.Pass ? "+" : "-",
                    conn,
                    detail);
                txtUsbLog.AppendText(msg);
            }
        }

        private void BuildDirectToolsView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };

            var cardModel = new CardPanel("Model-Specific Commands (Auto-Loaded)", Glyph.Bolt)
            {
                Dock = DockStyle.Top,
                Height = 220
            };

            pnlModelActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(6)
            };
            cardModel.Controls.Add(pnlModelActions);

            var cardCustom = new CardPanel("Send Raw Printer Command (ZPL / EPL / TSPL / ESC-POS)", Glyph.Code)
            {
                Dock = DockStyle.Fill
            };

            var flpBottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };

            txtCustomCmd = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.CardAlt,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Mono9,
                Text = "^XA^FO50,50^ADN,36,20^FDHello World^FS^XZ"
            };

            var btnSendRaw = new ModernButton("Send Raw Command to Printer", Glyph.Play, ButtonKind.Primary)
            {
                Dock = DockStyle.Bottom,
                Height = 42
            };
            btnSendRaw.Click += (s, e) =>
            {
                if (curPrinter == null) return;
                try
                {
                    byte[] data = ModelDatabase.ParseEscapes(txtCustomCmd.Text);
                    Native.SendRaw(curPrinter.Name, data, "Custom RAW Command");
                    Log("Sent " + data.Length + " raw bytes directly to " + curPrinter.Name);
                }
                catch (Exception ex)
                {
                    Log("SendRaw failed: " + ex.Message);
                }
            };

            flpBottom.Controls.Add(txtCustomCmd);
            flpBottom.Controls.Add(btnSendRaw);
            cardCustom.Controls.Add(flpBottom);

            pnl.Controls.Add(cardCustom);
            pnl.Controls.Add(cardModel);

            contentHost.Controls.Add(pnl);
            viewPanels["direct"] = pnl;
        }

        private void BuildReportView()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Bg };

            var card = new CardPanel("Generate Full Diagnostic Report", Glyph.Document)
            {
                Dock = DockStyle.Fill
            };

            var flp = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(20)
            };

            var lblDesc = new Label
            {
                Text = "Generates comprehensive diagnostic reports (.txt and .html) containing:\n\n" +
                       " • Complete Printer and Driver status & versions\n" +
                       " • Port and USB Plug-and-Play hardware connection\n" +
                       " • Print Spooler service health & queue contents\n" +
                       " • Microsoft-Windows-PrintService event logs (errors/warnings)\n" +
                       " • Official vendor support & driver download URLs\n",
                ForeColor = Theme.Text,
                Font = Theme.F10,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 20)
            };

            var btnGenReport = new ModernButton("Generate & View Diagnostic Report", Glyph.Document, ButtonKind.Primary)
            {
                Width = 320,
                Height = 46
            };
            btnGenReport.Click += async (s, e) =>
            {
                if (curPrinter == null) return;
                progBar.Running = true;
                Log("Generating full diagnostic report for " + curPrinter.Profile.DisplayName + "...");
                try
                {
                    string txtFile = await PrinterService.GenerateReport(curPrinter, curDriver, curHealth, curDevices, outputDir, Log);
                    Log("Report generated: " + txtFile);
                    Process.Start("notepad.exe", txtFile);
                }
                catch (Exception ex)
                {
                    Log("Report generation failed: " + ex.Message);
                }
                progBar.Running = false;
            };

            flp.Controls.AddRange(new Control[] { lblDesc, btnGenReport });
            card.Controls.Add(flp);

            pnl.Controls.Add(card);
            contentHost.Controls.Add(pnl);
            viewPanels["report"] = pnl;
        }

        // =====================================================================
        // DATA POPULATION & REFRESH LOGIC
        // =====================================================================
        private async Task RefreshPrintersListAsync()
        {
            progBar.Running = true;
            Log("Scanning installed printers and PnP devices...");

            allPrinters = await Task.Run(() => PrinterService.GetPrinters());

            cboPrinters.Items.Clear();
            cboTargetPrinter.Items.Clear();

            int bestIndex = 0;
            int highestScore = int.MinValue;

            for (int i = 0; i < allPrinters.Count; i++)
            {
                var p = allPrinters[i];
                cboPrinters.Items.Add(p.Name + " (" + p.Profile.DisplayName + ")");
                cboTargetPrinter.Items.Add(p.Name);

                int sc = PrinterService.Score(p);
                if (sc > highestScore)
                {
                    highestScore = sc;
                    bestIndex = i;
                }
            }

            if (allPrinters.Count > 0)
            {
                cboPrinters.SelectedIndex = bestIndex;
                cboTargetPrinter.SelectedIndex = bestIndex;
            }
            else
            {
                lblSelectedModel.Text = "No printers installed on this system.";
            }

            progBar.Running = false;
        }

        private async void OnPrinterSelectionChanged()
        {
            if (cboPrinters.SelectedIndex < 0 || cboPrinters.SelectedIndex >= allPrinters.Count) return;
            curPrinter = allPrinters[cboPrinters.SelectedIndex];
            lblSelectedModel.Text = curPrinter.Profile.DisplayName + " • " + curPrinter.Profile.Category;

            await RefreshDiagnosticsAsync();
            PopulateDirectCommands();
        }

        private async Task RefreshDiagnosticsAsync()
        {
            if (curPrinter == null) return;
            progBar.Running = true;

            curDriver = await Task.Run(() => PrinterService.GetDriver(curPrinter.DriverName));
            curDevices = await Task.Run(() => PrinterService.GetDevices(curPrinter));
            var jobs = await Task.Run(() => PrinterService.GetJobs(curPrinter.Name));
            curPrinter.JobCount = jobs.Count;

            HealthState portState;
            string connDetail;
            string conn = PrinterService.Connection(curPrinter, curDevices, out portState, out connDetail);

            HealthState printerState;
            string pStatus = PrinterService.StatusText(curPrinter, out printerState);

            string spoolState = PrinterService.SpoolerState();

            // Populate Hero
            heroCard.Brand = curPrinter.Profile.Brand;
            heroCard.Model = curPrinter.Profile.Model;
            heroCard.Category = curPrinter.Profile.Category;
            heroCard.Language = curPrinter.Profile.LanguageName;
            heroCard.Sub = "Queue: " + curPrinter.Name + "  •  Port: " + curPrinter.PortName;
            heroCard.ConnText = conn;
            heroCard.ConnColor = HealthList.ColorOf(portState);
            heroCard.StatusText = pStatus;
            heroCard.StatusColor = HealthList.ColorOf(printerState);
            heroCard.Invalidate();

            // Populate Stats
            tileDriver.Set(curDriver.Version ?? "Unknown", curDriver.Name, Theme.Accent);
            tilePort.Set(conn, curPrinter.PortName, HealthList.ColorOf(portState));
            tileSpooler.Set(spoolState, "Print Spooler", spoolState == "Running" ? Theme.Success : Theme.Danger);
            tileJobs.Set(curPrinter.JobCount.ToString(), curPrinter.JobCount > 0 ? "Jobs Pending" : "Queue Empty", curPrinter.JobCount > 0 ? Theme.Warning : Theme.Success);

            // Populate Health checks
            curHealth.Clear();
            curHealth.Add(new HealthItem(printerState, "Printer Queue Status", pStatus + (curPrinter.WorkOffline ? " (Work Offline is checked!)" : "")));
            curHealth.Add(new HealthItem(portState, "Hardware Connection", connDetail));
            curHealth.Add(new HealthItem(spoolState == "Running" ? HealthState.Pass : HealthState.Fail, "Print Spooler Service", "Spooler is currently " + spoolState));
            curHealth.Add(new HealthItem(curPrinter.JobCount == 0 ? HealthState.Pass : HealthState.Warn, "Print Queue Backlog", curPrinter.JobCount == 0 ? "No jobs stuck in queue" : curPrinter.JobCount + " jobs currently queued"));
            curHealth.Add(new HealthItem(curDriver.Found ? HealthState.Pass : HealthState.Warn, "Driver Installation", curDriver.Found ? "Driver registered (" + curDriver.Version + ")" : "Driver details could not be queried"));
            healthList.SetItems(curHealth);

            // Populate General Key-Values
            var kvs = new List<KeyValuePair<string, string>>
            {
                PrinterService.KV("Queue Name", curPrinter.Name),
                PrinterService.KV("Detected Model", curPrinter.Profile.DisplayName),
                PrinterService.KV("Manufacturer", curPrinter.Profile.BrandFull),
                PrinterService.KV("Category", curPrinter.Profile.Category),
                PrinterService.KV("Command Language", curPrinter.Profile.LanguageName),
                PrinterService.KV("Resolution", string.IsNullOrEmpty(curPrinter.Profile.Dpi) ? "Default" : curPrinter.Profile.Dpi),
                PrinterService.KV("Port Name", curPrinter.PortName),
                PrinterService.KV("Driver Name", curPrinter.DriverName),
                PrinterService.KV("Driver File Version", curDriver.FileVersion ?? "Unknown"),
                PrinterService.KV("Default Printer", curPrinter.IsDefault ? "Yes" : "No"),
                PrinterService.KV("Shared", curPrinter.IsShared ? "Yes" : "No"),
                PrinterService.KV("Support Link", curPrinter.Profile.SupportUrl)
            };
            kvGeneral.SetItems(kvs);

            // Populate Jobs Grid
            gridJobs.Rows.Clear();
            foreach (var j in jobs)
            {
                gridJobs.Rows.Add(j.Id, j.Document, j.Owner, j.Pages, (j.Size / 1024) + " KB", j.Status, j.Submitted);
            }

            // USB Live Monitor
            pulseMonitor.StateColor = HealthList.ColorOf(portState);
            pulseMonitor.Title = conn;
            pulseMonitor.Detail = connDetail;

            progBar.Running = false;
        }

        private void PopulateDirectCommands()
        {
            pnlModelActions.Controls.Clear();
            if (curPrinter == null) return;

            var cmds = ModelDatabase.CommandsFor(curPrinter.Profile);
            foreach (var cmd in cmds)
            {
                var btn = new ModernButton(cmd.Title, cmd.Glyph, cmd.Dangerous ? ButtonKind.Danger : ButtonKind.Secondary)
                {
                    Width = 230,
                    Height = 40
                };
                var c = cmd;
                btn.Click += (s, e) =>
                {
                    if (c.Dangerous)
                    {
                        var confirm = MessageBox.Show(
                            "This action (" + c.Title + ") will reset/modify printer configuration.\n\nAre you sure you want to proceed?",
                            "Confirm Printer Command", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (confirm != DialogResult.Yes) return;
                    }

                    try
                    {
                        byte[] data = ModelDatabase.ParseEscapes(c.Payload);
                        Native.SendRaw(curPrinter.Name, data, c.Title);
                        Log("Sent [" + c.Title + "] to " + curPrinter.Name);
                    }
                    catch (Exception ex)
                    {
                        Log("Command failed: " + ex.Message);
                    }
                };
                pnlModelActions.Controls.Add(btn);
            }
        }
    }
}
