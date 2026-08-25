using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

#if THEONE
[assembly: AssemblyTitle("더원 PC 케어 Universal")]
[assembly: AssemblyDescription("노트북과 데스크톱을 자동 판별하는 Windows 안전 정상화 도구")]
[assembly: AssemblyCompany("더원사업단")]
[assembly: AssemblyProduct("더원 PC 케어 Universal")]
[assembly: AssemblyCopyright("Copyright (c) 2026 더원사업단")]
#else
[assembly: AssemblyTitle("ReOne PC Care")]
[assembly: AssemblyDescription("Windows PC resource monitor and conservative safe-care tool")]
[assembly: AssemblyCompany("ReOne Partners")]
[assembly: AssemblyProduct("ReOne PC Care")]
[assembly: AssemblyCopyright("Copyright (c) 2026 ReOne Partners")]
#endif
[assembly: AssemblyVersion("3.1.0.0")]
[assembly: AssemblyFileVersion("3.1.0.0")]

namespace TheOnePcCare
{
    internal static class Branding
    {
#if THEONE
#if LAPTOP
        public const string ProductName = "더원 PC 케어 · 노트북";
        public const string BuildProfile = "노트북 고정";
#elif DESKTOP
        public const string ProductName = "더원 PC 케어 · 데스크톱";
        public const string BuildProfile = "데스크톱 고정";
#else
        public const string ProductName = "더원 PC 케어 · 범용";
        public const string BuildProfile = "자동 감지";
#endif
        public const string CompanyName = "더원사업단";
        public const string Eyebrow = "THE ONE · WINDOWS CARE SUITE";
        public const string DataCompany = "TheOne";
        public const string RegistryValue = "TheOnePcCareUniversalV3";
        public const string ErrorFileName = "더원_PC케어_오류.txt";
        public const string MutexName = "Local\\TheOnePcCareUniversalV3";
#else
        public const string ProductName = "ReOne PC Care";
        public const string CompanyName = "ReOne Partners";
        public const string Eyebrow = "REONE PARTNERS · WINDOWS SAFE CARE";
        public const string DataCompany = "ReOnePartners";
        public const string RegistryValue = "ReOnePcCareV2";
        public const string ErrorFileName = "ReOne_PC_Care_Error.txt";
        public const string MutexName = "Local\\ReOnePcCareV2";
#endif
        public const string Version = "3.1.0";
        public const string IconResourceName = "PcCareIcon";

        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    DataCompany,
                    "PCCareUniversal");
            }
        }

        public static Image LoadAppImage()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName))
                {
                    if (stream == null) return null;
                    using (Image source = Image.FromStream(stream)) return new Bitmap(source);
                }
            }
            catch { return null; }
        }

        public static Icon CreateIcon()
        {
            try
            {
                using (Image source = LoadAppImage())
                using (Bitmap bitmap = new Bitmap(64, 64))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    if (source != null) graphics.DrawImage(source, new Rectangle(0, 0, 64, 64));
                    else
                    {
                        using (SolidBrush brush = new SolidBrush(Palette.Teal)) graphics.FillEllipse(brush, 2, 2, 60, 60);
                        using (Font font = new Font("Arial", 22f, FontStyle.Bold))
                        using (SolidBrush white = new SolidBrush(Color.White)) graphics.DrawString("PC", font, white, 8, 16);
                    }
                    IntPtr handle = bitmap.GetHicon();
                    try { return (Icon)Icon.FromHandle(handle).Clone(); }
                    finally { NativeMethods.DestroyIcon(handle); }
                }
            }
            catch { return SystemIcons.Application; }
        }
    }

    internal static class Palette
    {
        public static readonly Color Background = ColorTranslator.FromHtml("#F5F5F7");
        public static readonly Color White = Color.White;
        public static readonly Color Charcoal = ColorTranslator.FromHtml("#2C2C2C");
        public static readonly Color Gray = ColorTranslator.FromHtml("#6B6B6B");
        public static readonly Color Teal = ColorTranslator.FromHtml("#4FA38C");
        public static readonly Color TealLight = ColorTranslator.FromHtml("#EAF6F1");
        public static readonly Color Yellow = ColorTranslator.FromHtml("#D6A21C");
        public static readonly Color YellowLight = ColorTranslator.FromHtml("#FFF4D6");
        public static readonly Color Red = ColorTranslator.FromHtml("#C0392B");
        public static readonly Color RedLight = ColorTranslator.FromHtml("#FDECEA");
        public static readonly Color Unknown = ColorTranslator.FromHtml("#9A9A9A");
        public static readonly Color UnknownLight = ColorTranslator.FromHtml("#EEEEF0");

        public static Color Accent(HealthLevel level)
        {
            if (level == HealthLevel.Warning) return Red;
            if (level == HealthLevel.Caution) return Yellow;
            if (level == HealthLevel.Normal) return Teal;
            return Unknown;
        }

        public static Color Surface(HealthLevel level)
        {
            if (level == HealthLevel.Warning) return RedLight;
            if (level == HealthLevel.Caution) return YellowLight;
            if (level == HealthLevel.Normal) return TealLight;
            return UnknownLight;
        }
    }

    internal enum DeviceProfile
    {
        Laptop,
        Desktop,
        OfficeCorea
    }

    internal static class ProfileDetector
    {
        private static DeviceProfile? cached;

        public static DeviceProfile Current
        {
            get
            {
                if (cached.HasValue) return cached.Value;
#if LAPTOP
                cached = DeviceProfile.Laptop;
#elif DESKTOP
                cached = string.Equals(Environment.MachineName, "COREA", StringComparison.OrdinalIgnoreCase)
                    ? DeviceProfile.OfficeCorea
                    : DeviceProfile.Desktop;
#else
                if (string.Equals(Environment.MachineName, "COREA", StringComparison.OrdinalIgnoreCase))
                    cached = DeviceProfile.OfficeCorea;
                else cached = IsLaptopHardware() ? DeviceProfile.Laptop : DeviceProfile.Desktop;
#endif
                return cached.Value;
            }
        }

        public static string DisplayName
        {
            get
            {
                if (Current == DeviceProfile.OfficeCorea) return "COREA 사무실 프로필";
                if (Current == DeviceProfile.Laptop) return "노트북 프로필";
                return "데스크톱 프로필";
            }
        }

        public static string Description
        {
            get
            {
                if (Current == DeviceProfile.OfficeCorea) return "OneDrive · 탐색기 · RICOH · 검색색인 · 백그라운드 부하";
                if (Current == DeviceProfile.Laptop) return "OneDrive · 탐색기 · 절전 · 폰 연결 · 카메라 유틸리티";
                return "OneDrive · 탐색기 · 검색색인 · 백그라운드 작업";
            }
        }

        private static bool IsLaptopHardware()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        ushort[] types = item["ChassisTypes"] as ushort[];
                        if (types == null) continue;
                        foreach (ushort type in types)
                        {
                            if ((type >= 8 && type <= 14) || type == 18 || type == 21 || type == 30 || type == 31 || type == 32)
                                return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_Battery"))
                using (ManagementObjectCollection results = searcher.Get()) return results.Count > 0;
            }
            catch { return false; }
        }
    }

    internal enum HealthLevel
    {
        Unknown = 0,
        Normal = 1,
        Caution = 2,
        Warning = 3
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool commandLineMode = args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal);
            try
            {
                if (args.Length > 0 && (args[0] == "--self-test" || args[0] == "--snapshot"))
                {
                    RunSelfTest(args);
                    Environment.Exit(Environment.ExitCode);
                    return;
                }

                if (args.Length > 1 && (args[0] == "--ui-snapshot" || args[0] == "--widget-snapshot"))
                {
                    RunUiSnapshot(args[0], args[1], args.Length > 2 ? args[2] : "live");
                    Environment.Exit(0);
                    return;
                }

                if (args.Length > 0 && args[0] == "--elevated-office-care")
                {
                    string resultPath;
                    if (args.Length < 2 || !TryValidateElevatedResultPath(args[1], out resultPath))
                    {
                        Environment.Exit(3);
                        return;
                    }
                    bool pauseStt = args.Length > 2 && string.Equals(args[2], "--pause-stt", StringComparison.OrdinalIgnoreCase);
                    string result = OfficeCareEngine.RunElevatedStage(pauseStt);
                    using (FileStream stream = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.Write(result);
                    Environment.Exit(0);
                    return;
                }

                if (args.Length > 0 && args[0] == "--office-plan")
                {
                    string target = args.Length > 1
                        ? args[1]
                        : Path.Combine(Path.GetTempPath(), "pc_care_corea_plan.txt");
                    File.WriteAllText(target, OfficeCareEngine.BuildPlan(), new UTF8Encoding(true));
                    Environment.Exit(0);
                    return;
                }

                bool startHidden = false;
                bool created;
                using (Mutex singleInstance = new Mutex(true, Branding.MutexName, out created))
                {
                    if (!created)
                    {
                        MessageBox.Show("이미 실행 중입니다. 작업 표시줄에서 프로그램 창을 확인하세요.", Branding.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm(startHidden, false));
                    GC.KeepAlive(singleInstance);
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), Branding.ErrorFileName), ex.ToString(), Encoding.UTF8); }
                catch { }
                Environment.ExitCode = 1;
                if (commandLineMode) return;
                MessageBox.Show(
                    "프로그램 실행 중 오류가 발생했습니다.\r\n\r\n" + ex.Message,
                    Branding.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool TryValidateElevatedResultPath(string candidate, out string validated)
        {
            validated = "";
            try
            {
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(candidate);
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(Path.GetDirectoryName(full).TrimEnd(Path.DirectorySeparatorChar), temp.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return false;
                string name = Path.GetFileName(full);
                const string prefix = "pc_care_v2_";
                const string suffix = ".txt";
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
                string token = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
                if (token.Length != 32 || token.Any(delegate(char value) { return !Uri.IsHexDigit(value); })) return false;
                if (File.Exists(full)) return false;
                validated = full;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RunSelfTest(string[] args)
        {
            CpuSampler cpu = new CpuSampler();
            DiskSampler disk = new DiskSampler();
            cpu.Sample();
            disk.Sample();
            Thread.Sleep(900);
            Snapshot snapshot = Snapshot.Capture(cpu.Sample(), disk.Sample());
            string target = args.Length > 1
                ? args[1]
                : Path.Combine(Path.GetTempPath(), "pc_care_v2_selftest.txt");
            StringBuilder report = new StringBuilder();
            report.AppendLine(Branding.ProductName + " v" + Branding.Version + " 자체진단");
            report.AppendLine("실행 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            List<string> failures = RunPolicyAssertions();
            report.AppendLine("판정: " + (failures.Count == 0 ? "PASS" : "FAIL"));
            report.AppendLine("범용 안전 정상화 정책: 사용자 파일 삭제 안 함 / 문서·백신·브라우저·Office 종료 안 함");
            report.AppendLine("프로필: " + ProfileDetector.DisplayName + " / OneDrive·탐색기는 임계값을 넘을 때만 재시작 / 중요 작업은 별도 동의");
            report.AppendLine();
            report.Append(snapshot.ToReport());
            if (failures.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("실패 항목:");
                foreach (string failure in failures) report.AppendLine("- " + failure);
            }
            File.WriteAllText(target, report.ToString(), new UTF8Encoding(true));
            if (failures.Count > 0) Environment.ExitCode = 2;
        }

        private static List<string> RunPolicyAssertions()
        {
            List<string> failures = new List<string>();
            AssertRule(failures, "CPU 정상", SnapshotRules.HighPercent(69, 70, 90) == HealthLevel.Normal);
            AssertRule(failures, "CPU 주의", SnapshotRules.HighPercent(70, 70, 90) == HealthLevel.Caution);
            AssertRule(failures, "CPU 경고", SnapshotRules.HighPercent(90, 70, 90) == HealthLevel.Warning);
            AssertRule(failures, "시스템 핸들 정상", SnapshotRules.HighLong(249999, 250000, 500000) == HealthLevel.Normal);
            AssertRule(failures, "시스템 핸들 주의", SnapshotRules.HighLong(250000, 250000, 500000) == HealthLevel.Caution);
            AssertRule(failures, "시스템 핸들 경고", SnapshotRules.HighLong(500000, 250000, 500000) == HealthLevel.Warning);
            AssertRule(failures, "C드라이브 105GB/11% 정상", SnapshotRules.DriveFree(105, 11) == HealthLevel.Normal);
            AssertRule(failures, "C드라이브 19GB/14% 주의", SnapshotRules.DriveFree(19, 14) == HealthLevel.Caution);
            AssertRule(failures, "C드라이브 4GB 경고", SnapshotRules.DriveFree(4, 50) == HealthLevel.Warning);
            AssertRule(failures, "C드라이브 9GB/7% 경고", SnapshotRules.DriveFree(9, 7) == HealthLevel.Warning);
            AssertRule(failures, "OneDrive 249,999 핸들은 재시작 금지", !OfficeCareEngine.ShouldRestartOneDrive(249999));
            AssertRule(failures, "OneDrive 250,000 핸들은 재시작 허용", OfficeCareEngine.ShouldRestartOneDrive(250000));
            AssertRule(failures, "탐색기 19,999 핸들은 재시작 금지", !OfficeCareEngine.ShouldRestartExplorer(19999, 299));
            AssertRule(failures, "탐색기 20,000 핸들은 재시작 허용", OfficeCareEngine.ShouldRestartExplorer(20000, 100));
            AssertRule(failures, "탐색기 300 스레드는 재시작 허용", OfficeCareEngine.ShouldRestartExplorer(1000, 300));
            AssertRule(failures, "파일 삭제 기능 없음", !OfficeCareEngine.HasFileDeletionCapability);

            string validatedResultPath;
            string validResultPath = Path.Combine(Path.GetTempPath(), "pc_care_v2_" + Guid.NewGuid().ToString("N") + ".txt");
            AssertRule(failures, "관리자 결과 경로 정상 형식 허용", TryValidateElevatedResultPath(validResultPath, out validatedResultPath));
            AssertRule(failures, "관리자 결과 경로 임의 위치 차단", !TryValidateElevatedResultPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "pc_care_v2_00000000000000000000000000000000.txt"), out validatedResultPath));

            SustainedMeter meter = new SustainedMeter(10, 20, 20);
            HealthLevel firstSpike = meter.Update(95, 70, 90);
            HealthLevel beforeWarning = firstSpike;
            for (int i = 1; i < 19; i++) beforeWarning = meter.Update(95, 70, 90);
            HealthLevel sustainedWarning = meter.Update(95, 70, 90);
            AssertRule(failures, "순간 1회 급등은 경고 금지", firstSpike != HealthLevel.Warning);
            AssertRule(failures, "CPU 60초 미만은 경고 금지", beforeWarning != HealthLevel.Warning);
            AssertRule(failures, "CPU 60초 지속 급등은 경고", sustainedWarning == HealthLevel.Warning);
            HealthLevel released = sustainedWarning;
            for (int i = 0; i < 20; i++) released = meter.Update(60, 70, 90);
            AssertRule(failures, "20회 안정 후 경고 해제", released == HealthLevel.Normal);
            return failures;
        }

        private static void AssertRule(List<string> failures, string name, bool passed)
        {
            if (!passed) failures.Add(name);
        }

        private static void RunUiSnapshot(string mode, string target, string state)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (mode == "--widget-snapshot")
            {
                CpuSampler cpu = new CpuSampler();
                DiskSampler disk = new DiskSampler();
                cpu.Sample(); disk.Sample(); Thread.Sleep(800);
                Snapshot snapshot = Snapshot.Capture(cpu.Sample(), disk.Sample());
                using (WidgetForm widget = new WidgetForm(delegate { }, delegate { }, delegate { }, delegate { }))
                using (Bitmap bitmap = new Bitmap(widget.Width, widget.Height))
                {
                    widget.StartPosition = FormStartPosition.Manual;
                    widget.Location = new Point(-32000, -32000);
                    HealthEvaluation evaluation = HealthEvaluation.Immediate(snapshot, 0);
                    widget.UpdateSnapshot(snapshot, evaluation, 0);
                    widget.Show();
                    Application.DoEvents();
                    widget.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    widget.Hide();
                    bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                }
                return;
            }

            using (MainForm form = new MainForm(false, true))
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                Application.DoEvents();
                form.PrepareForSnapshot(state);
                Application.DoEvents();
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                form.Hide();
                bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public int Radius = 16;

        public RoundedPanel()
        {
            DoubleBuffered = true;
            Resize += delegate { RefreshRegion(); };
        }

        private void RefreshRegion()
        {
            if (Width <= 1 || Height <= 1) return;
            int radius = Math.Max(2, Math.Min(Radius, Math.Min(Width, Height) / 2));
            int diameter = radius * 2;
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddArc(0, 0, diameter, diameter, 180, 90);
                path.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
                path.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
                path.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
                path.CloseFigure();
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        public int Radius = 12;

        public RoundedButton()
        {
            Resize += delegate { RefreshRegion(); };
        }

        private void RefreshRegion()
        {
            if (Width <= 1 || Height <= 1) return;
            int radius = Math.Max(2, Math.Min(Radius, Math.Min(Width, Height) / 2));
            int diameter = radius * 2;
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddArc(0, 0, diameter, diameter, 180, 90);
                path.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
                path.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
                path.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
                path.CloseFigure();
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }

    internal sealed class MetricCard
    {
        private readonly Panel panel;
        private readonly Panel line;
        private readonly Label caption;
        private readonly Label value;
        private readonly Label detail;

        public MetricCard(TableLayoutPanel host, int column, string title)
        {
            panel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Palette.White,
                Margin = new Padding(column == 0 ? 0 : 6, 0, column == 5 ? 0 : 6, 0),
                Padding = new Padding(14)
            };
            line = new Panel { BackColor = Palette.Unknown, Height = 4, Dock = DockStyle.Top };
            caption = new Label { Text = title, AutoSize = true, ForeColor = Palette.Gray, Location = new Point(14, 20) };
            value = new Label
            {
                Text = "측정 중",
                AutoSize = true,
                ForeColor = Palette.Charcoal,
                Font = new Font("Malgun Gothic", 15f, FontStyle.Bold),
                Location = new Point(13, 47)
            };
            detail = new Label
            {
                Text = "",
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Palette.Gray,
                Font = new Font("Malgun Gothic", 8f),
                Location = new Point(14, 80),
                Size = new Size(145, 21),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            panel.Controls.Add(line);
            panel.Controls.Add(caption);
            panel.Controls.Add(value);
            panel.Controls.Add(detail);
            panel.Resize += delegate { detail.Width = Math.Max(50, panel.ClientSize.Width - 28); };
            host.Controls.Add(panel, column, 0);
        }

        public void Apply(string display, string note, HealthLevel level)
        {
            value.Text = display;
            detail.Text = note;
            panel.BackColor = Palette.White;
            line.BackColor = Palette.Accent(level);
            caption.ForeColor = level == HealthLevel.Unknown ? Palette.Gray : Palette.Accent(level);
        }
    }

    internal sealed class ElevatedOutcome
    {
        public bool Cancelled;
        public bool StillRunning;
        public string Result = "";
        public string Error = "";
    }

    internal sealed class MainForm : Form
    {
        private readonly CpuSampler cpuSampler = new CpuSampler();
        private readonly DiskSampler diskSampler = new DiskSampler();
        private readonly SustainedHealth sustainedHealth = new SustainedHealth();
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly bool captureMode;
        private Icon formIcon;
        private Icon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem widgetMenuItem;
        private MetricCard cpuCard;
        private MetricCard ramCard;
        private MetricCard commitCard;
        private MetricCard diskCard;
        private MetricCard handleCard;
        private MetricCard driveCard;
        private Label statusPill;
        private Label protectedBanner;
        private Label diagnosis;
        private Label protectedList;
        private Label cpuHotspotLabel;
        private TextBox logBox;
        private CheckBox widgetCheck;
        private CheckBox alertCheck;
        private CheckBox startupCheck;
        private WidgetForm widget;
        private bool allowClose;
        private bool busy;
        private bool resourcesDisposed;
        private int maximStreak;
        private DateTime lastAlert = DateTime.MinValue;
        private readonly ProcessCpuSampler processCpuSampler = new ProcessCpuSampler();
        private CpuHotspot lastHotspot = new CpuHotspot();

        public MainForm(bool startHidden, bool captureModeValue)
        {
            captureMode = captureModeValue;
            Text = Branding.ProductName + " v" + Branding.Version;
            Font = new Font("Malgun Gothic", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Palette.Background;
            ForeColor = Palette.Charcoal;
            MinimumSize = new Size(1120, 740);
            Size = new Size(1240, 820);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            formIcon = Branding.CreateIcon();
            Icon = formIcon;
            BuildUi();
            widgetCheck = new CheckBox { Checked = false, Visible = false };
            alertCheck = new CheckBox { Checked = false, Visible = false };
            startupCheck = new CheckBox { Checked = false, Visible = false };

            refreshTimer.Interval = 3000;
            refreshTimer.Tick += delegate { RefreshStatus(false); };
            if (!captureMode) refreshTimer.Start();
            Shown += delegate
            {
                if (captureMode) return;
                RefreshStatus(true);
            };
        }

        public void PrepareForSnapshot(string state)
        {
            if (!string.Equals(state, "live", StringComparison.OrdinalIgnoreCase))
            {
                Snapshot synthetic = Snapshot.CreateForUiState(state);
                maximStreak = string.Equals(state, "warning", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                lastHotspot = string.Equals(state, "warning", StringComparison.OrdinalIgnoreCase)
                    ? new CpuHotspot { Available = true, Summary = "Windows 탐색기 36% · Python/STT 17% · OneDrive 8%" }
                    : new CpuHotspot { Available = true, Summary = "Chrome 8% · 시스템 3%" };
                HealthEvaluation evaluation = HealthEvaluation.Immediate(synthetic, maximStreak);
                ApplySnapshot(synthetic, evaluation);
                return;
            }
            cpuSampler.Sample(); diskSampler.Sample(); processCpuSampler.Sample(); Thread.Sleep(850);
            Snapshot live = Snapshot.Capture(cpuSampler.Sample(), diskSampler.Sample());
            lastHotspot = processCpuSampler.Sample();
            ApplySnapshot(live, sustainedHealth.Update(live, maximStreak));
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22),
                BackColor = Palette.Background,
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            Controls.Add(root);

            Panel header = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background };
            PictureBox icon = new PictureBox
            {
                Image = Branding.LoadAppImage(),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Location = new Point(0, 0),
                Size = new Size(82, 82)
            };
            Label eyebrow = new Label
            {
                Text = Branding.Eyebrow,
                AutoSize = true,
                Location = new Point(99, 4),
                Font = new Font("Malgun Gothic", 8.5f, FontStyle.Bold),
                ForeColor = Palette.Teal
            };
            Label title = new Label
            {
                Text = Branding.ProductName,
                AutoSize = true,
                Location = new Point(97, 24),
                Font = new Font("Malgun Gothic", 22f, FontStyle.Bold),
                ForeColor = Palette.Charcoal
            };
            Label subtitle = new Label
            {
                Text = ProfileDetector.Description + "를 안전하게 관리합니다.",
                AutoSize = false,
                Size = new Size(760, 28),
                Location = new Point(100, 66),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Malgun Gothic", 9.4f),
                ForeColor = Palette.Gray,
                AutoEllipsis = true
            };
            statusPill = new Label
            {
                Text = "측정 중",
                AutoSize = false,
                Size = new Size(154, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Palette.Unknown,
                ForeColor = Palette.White,
                Font = new Font("Malgun Gothic", 10f, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(970, 14)
            };
            Label profilePill = new Label
            {
                Text = ProfileDetector.DisplayName,
                AutoSize = false,
                Size = new Size(184, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Palette.TealLight,
                ForeColor = Palette.Teal,
                Font = new Font("Malgun Gothic", 8.7f, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(938, 57)
            };
            header.Controls.Add(icon);
            header.Controls.Add(eyebrow);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(statusPill);
            header.Controls.Add(profilePill);
            header.Resize += delegate
            {
                statusPill.Left = Math.Max(0, header.ClientSize.Width - statusPill.Width);
                profilePill.Left = Math.Max(0, header.ClientSize.Width - profilePill.Width);
            };
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel cards = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(0, 0, 0, 10)
            };
            for (int i = 0; i < 6; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.6667f));
            cpuCard = new MetricCard(cards, 0, "CPU");
            ramCard = new MetricCard(cards, 1, "RAM 사용");
            commitCard = new MetricCard(cards, 2, "메모리 한도");
            diskCard = new MetricCard(cards, 3, "디스크 활동");
            handleCard = new MetricCard(cards, 4, "시스템 자원");
            driveCard = new MetricCard(cards, 5, "C: 여유");
            root.Controls.Add(cards, 0, 1);

            protectedBanner = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 10),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 16, 0),
                BackColor = Palette.TealLight,
                ForeColor = Palette.Charcoal,
                Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold),
                AutoEllipsis = true
            };
            root.Controls.Add(protectedBanner, 0, 2);

            TableLayoutPanel body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Palette.Background
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            root.Controls.Add(body, 0, 3);

            TableLayoutPanel left = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0, 0, 8, 0)
            };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(left, 0, 0);

            Panel diagnosisPanel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Palette.White,
                Padding = new Padding(20, 14, 20, 12),
                Margin = new Padding(0, 0, 0, 10)
            };
            Label diagnosisTitle = new Label
            {
                Text = "현재 판단",
                AutoSize = true,
                Font = new Font("Malgun Gothic", 10f, FontStyle.Bold),
                ForeColor = Palette.Teal,
                Location = new Point(20, 14)
            };
            diagnosis = new Label
            {
                Text = "상태를 확인하고 있습니다.",
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(20, 43),
                Height = 28,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = Palette.Charcoal
            };
            cpuHotspotLabel = new Label
            {
                Text = "실시간 CPU 주범을 측정하고 있습니다.",
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(20, 76),
                Height = 24,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                Font = new Font("Malgun Gothic", 8.6f, FontStyle.Bold),
                ForeColor = Palette.Gray
            };
            diagnosisPanel.Controls.Add(diagnosisTitle);
            diagnosisPanel.Controls.Add(diagnosis);
            diagnosisPanel.Controls.Add(cpuHotspotLabel);
            diagnosisPanel.Resize += delegate
            {
                diagnosis.Width = Math.Max(100, diagnosisPanel.ClientSize.Width - 40);
                cpuHotspotLabel.Width = Math.Max(100, diagnosisPanel.ClientSize.Width - 40);
            };
            left.Controls.Add(diagnosisPanel, 0, 0);

            Panel protectionPanel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Palette.White,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(20, 11, 20, 8)
            };
            Label protectionTitle = new Label
            {
                Text = "안전 보호 원칙",
                AutoSize = true,
                Font = new Font("Malgun Gothic", 9f, FontStyle.Bold),
                ForeColor = Palette.Gray,
                Location = new Point(20, 11)
            };
            protectedList = new Label
            {
                Text = "문서 · 백신 · 브라우저 · Office는 보호하고, 위험한 작업은 별도 확인 후에만 조치합니다.",
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(20, 36),
                Height = 25,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = Palette.Charcoal
            };
            protectionPanel.Controls.Add(protectionTitle);
            protectionPanel.Controls.Add(protectedList);
            protectionPanel.Resize += delegate { protectedList.Width = Math.Max(100, protectionPanel.ClientSize.Width - 40); };
            left.Controls.Add(protectionPanel, 0, 1);

            TableLayoutPanel logPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Palette.Background
            };
            logPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label logTitle = new Label
            {
                Text = "활동 기록",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold),
                ForeColor = Palette.Charcoal
            };
            logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Palette.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Malgun Gothic", 9f),
                ForeColor = Palette.Charcoal
            };
            logPanel.Controls.Add(logTitle, 0, 0);
            logPanel.Controls.Add(logBox, 0, 1);
            left.Controls.Add(logPanel, 0, 2);

            Panel actionCard = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Palette.White,
                Padding = new Padding(18),
                Margin = new Padding(8, 0, 0, 0)
            };
            TableLayoutPanel actionLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7
            };
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actionCard.Controls.Add(actionLayout);
            Label actionTitle = new Label
            {
                Text = "PC 안전 정상화",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Malgun Gothic", 15f, FontStyle.Bold),
                ForeColor = Palette.Charcoal
            };
            actionLayout.Controls.Add(actionTitle, 0, 0);
            Button primary = MakeButton("안전 정상화 실행", Palette.Teal, OfficeCare, 11f);
            primary.Margin = new Padding(0, 0, 0, 10);
            actionLayout.Controls.Add(primary, 0, 1);
            Label policy = new Label
            {
                Text = "OneDrive·탐색기 폭주를 확인해 정상화합니다.\r\n무거운 중요 작업은 정체를 표시하고 별도 동의를 받습니다.\r\n사용자 파일은 삭제하지 않습니다.",
                Dock = DockStyle.Fill,
                ForeColor = Palette.Gray,
                Font = new Font("Malgun Gothic", 8.6f),
                Padding = new Padding(2, 0, 0, 0)
            };
            actionLayout.Controls.Add(policy, 0, 2);
            actionLayout.Controls.Add(MakeButton("현재 상태 복사", Palette.Charcoal, CopyStatus, 9f), 0, 3);
            actionLayout.Controls.Add(MakeButton("기록 폴더 열기", Palette.Yellow, OpenLogFolder, 9f), 0, 4);
            actionLayout.Controls.Add(MakeButton("진단 보고서 저장", Palette.Gray, SaveDiagnosticReport, 9f), 0, 5);
            Label help = new Label
            {
                Text = "현재 장치를 자동 판별했습니다: " + ProfileDetector.DisplayName + ". 조치 전 계획을 먼저 보여주며, 노트북과 데스크톱에서 같은 안전 기준을 적용합니다.",
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 0, 0),
                ForeColor = Palette.Gray,
                Font = new Font("Malgun Gothic", 8.1f),
                AutoEllipsis = true
            };
            actionLayout.Controls.Add(help, 0, 6);
            body.Controls.Add(actionCard, 1, 0);

            Panel footer = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background };
            Label profile = new Label
            {
                Text = ProfileDetector.DisplayName + " · " + Branding.BuildProfile + " · 상시 위젯 없음",
                AutoSize = true,
                ForeColor = Palette.Gray,
                Location = new Point(0, 9)
            };
            Label version = new Label
            {
                Text = "v" + Branding.Version + " · " + Branding.CompanyName,
                AutoSize = true,
                ForeColor = Palette.Gray,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(900, 9)
            };
            footer.Controls.Add(profile);
            footer.Controls.Add(version);
            footer.Resize += delegate { version.Left = Math.Max(0, footer.ClientSize.Width - version.Width); };
            root.Controls.Add(footer, 0, 4);
        }

        private Button MakeButton(string text, Color color, EventHandler handler, float size)
        {
            Button button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = color,
                ForeColor = Palette.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Malgun Gothic", size, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(color, .08f);
            button.Click += handler;
            return button;
        }

        private void BuildTray()
        {
            trayIcon = Branding.CreateIcon();
            tray.Icon = trayIcon;
            tray.Text = Branding.ProductName;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("대시보드 열기", null, delegate { ShowWindow(); });
            widgetMenuItem = new ToolStripMenuItem("상시 위젯 표시");
            widgetMenuItem.Checked = widgetCheck.Checked;
            widgetMenuItem.Click += delegate { SetWidgetVisible(!widgetCheck.Checked, true); };
            trayMenu.Items.Add(widgetMenuItem);
            trayMenu.Items.Add("사무실 PC 정상화", null, delegate { OfficeCare(this, EventArgs.Empty); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("프로그램 완전히 종료", null, delegate { RequestTrayExit(); });
            tray.ContextMenuStrip = trayMenu;
        }

        private void EnsureWidget()
        {
            if (captureMode || !widgetCheck.Checked) return;
            if (widget == null || widget.IsDisposed)
            {
                widget = new WidgetForm(
                    delegate { ShowWindow(); },
                    delegate { OfficeCare(this, EventArgs.Empty); },
                    delegate { SetWidgetVisible(false, true); },
                    delegate { RequestWidgetExit(); });
                widget.Show();
            }
            else if (!widget.Visible) widget.Show();
        }

        private void SetWidgetVisible(bool visible, bool persist)
        {
            widgetCheck.Checked = visible;
            if (widgetMenuItem != null) widgetMenuItem.Checked = visible;
            if (persist) WidgetPreference.Save(visible);
            if (visible) EnsureWidget();
            else if (widget != null && !widget.IsDisposed) widget.Hide();
        }

        private void RequestWidgetExit()
        {
            DialogResult choice = MessageBox.Show(
                "프로그램을 어떻게 할까요?\r\n\r\n예: 프로그램 완전 종료\r\n아니요: 위젯만 숨기기\r\n취소: 계속 사용",
                Branding.ProductName,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (choice == DialogResult.Yes) ExitApplication();
            else if (choice == DialogResult.No) SetWidgetVisible(false, true);
        }

        private void RequestTrayExit()
        {
            if (MessageBox.Show(
                "프로그램을 완전히 종료할까요?\r\n상시 위젯과 주의·경고 알림도 함께 꺼집니다.",
                Branding.ProductName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                ExitApplication();
        }

        private void ExitApplication()
        {
            allowClose = true;
            if (widget != null && !widget.IsDisposed) widget.Close();
            tray.Visible = false;
            Close();
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (captureMode) return;
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult choice = MessageBox.Show(
                    "프로그램을 어떻게 할까요?\r\n\r\n예: 프로그램 완전 종료\r\n아니요: 대시보드만 숨기고 위젯·알림 계속\r\n취소: 돌아가기",
                    Branding.ProductName,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (choice == DialogResult.Yes) allowClose = true;
                else
                {
                    e.Cancel = true;
                    if (choice == DialogResult.No)
                    {
                        Hide();
                        if (widgetCheck.Checked) EnsureWidget();
                        tray.ShowBalloonTip(1800, Branding.ProductName, "대시보드만 숨겼습니다. 완전 종료는 위젯 × 또는 트레이 메뉴에서 할 수 있습니다.", ToolTipIcon.Info);
                    }
                    return;
                }
            }
            tray.Visible = false;
            if (widget != null && !widget.IsDisposed) widget.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true;
                refreshTimer.Stop();
                refreshTimer.Dispose();
                diskSampler.Dispose();
                if (widget != null && !widget.IsDisposed)
                {
                    widget.Close();
                    widget.Dispose();
                }
                tray.Visible = false;
                tray.Dispose();
                if (trayMenu != null) trayMenu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
                if (formIcon != null) { formIcon.Dispose(); formIcon = null; }
            }
        }

        private void RefreshStatus(bool forceLog)
        {
            if (busy) return;
            try
            {
                Snapshot snapshot = Snapshot.Capture(cpuSampler.Sample(), diskSampler.Sample());
                lastHotspot = processCpuSampler.Sample();
                if (!captureMode) maximStreak = MaximGuard.Observe(snapshot.Maxim);
                HealthEvaluation evaluation = sustainedHealth.Update(snapshot, maximStreak);
                ApplySnapshot(snapshot, evaluation);
                if (forceLog)
                {
                    AppendLog(
                        "상태 확인 · CPU " + snapshot.CpuPercent.ToString("0") + "% · RAM " +
                        snapshot.MemoryUsedPercent.ToString("0") + "% · 커밋 " +
                        snapshot.CommitPercent.ToString("0") + "% · 핸들 " + snapshot.HandleCount.ToString("N0"));
                }
            }
            catch (Exception ex)
            {
                if (forceLog) AppendLog("상태 확인 실패: " + ex.Message);
            }
        }

        private void ApplySnapshot(Snapshot s, HealthEvaluation evaluation)
        {
            HealthLevel cpuLevel = evaluation.Cpu;
            HealthLevel ramLevel = evaluation.Ram;
            HealthLevel commitLevel = SnapshotRules.HighPercent(s.CommitPercent, 75, 90);
            HealthLevel diskLevel = evaluation.Disk;
            HealthLevel handleLevel = SnapshotRules.HighLong(s.HandleCount, 250000, 500000);
            HealthLevel driveLevel = SnapshotRules.DriveFree(s.SystemDriveFreeGb, s.SystemDriveFreePercent);

            cpuCard.Apply(s.CpuPercent.ToString("0") + "%", "30초 주의 · 60초 경고", cpuLevel);
            ramCard.Apply(s.MemoryUsedPercent.ToString("0") + "%", "가용 " + s.AvailableGb.ToString("0.0") + "GB · 주의 80%", ramLevel);
            commitCard.Apply(s.CommitPercent.ToString("0") + "%", "주의 75% · 경고 90%", commitLevel);
            diskCard.Apply(s.DiskAvailable ? s.DiskPercent.ToString("0") + "%" : "--", s.DiskAvailable ? "60초 주의 · 120초 경고" : "측정 지원 안 됨", diskLevel);
            handleCard.Apply(s.HandleCount.ToString("N0"), "프로세스 " + s.ProcessCount.ToString("N0") + "개", handleLevel);
            driveCard.Apply(s.SystemDriveFreeGb.ToString("0.0") + "GB", s.SystemDriveFreePercent.ToString("0") + "% 여유", driveLevel);

            HealthLevel overall = evaluation.Overall;
            statusPill.Text = overall == HealthLevel.Warning ? "경고 · 확인 필요" : overall == HealthLevel.Caution ? "주의 · 살펴보기" : "정상 · 안정적";
            statusPill.BackColor = Palette.Accent(overall);

            List<string> protectedActive = s.GetActiveProtectedNames();
            bool oneDriveRunaway = string.Equals(s.TopHandleProcess, "OneDrive", StringComparison.OrdinalIgnoreCase) && OfficeCareEngine.ShouldRestartOneDrive(s.TopHandleCount);
            bool sttIdleManaged = OfficeCareEngine.IsSttIdlePolicyCurrent();
            string sttIdleText = OfficeCareEngine.GetSttIdlePolicyDisplay();
            if (oneDriveRunaway)
            {
                protectedActive.RemoveAll(delegate(string name) { return string.Equals(name, "OneDrive", StringComparison.OrdinalIgnoreCase); });
                protectedBanner.Text = "OneDrive 자원 폭주 " + s.TopHandleCount.ToString("N0") + "개 — 안전 정상화에서 확인 후 재시작할 수 있습니다.";
                protectedBanner.BackColor = Palette.RedLight;
            }
            else if (sttIdleManaged)
            {
                protectedBanner.Text = "전사 자동관리 · " + sttIdleText + " — PC 사용 중에는 멈추고 10분 무입력 후 재개합니다.";
                protectedBanner.BackColor = Palette.TealLight;
            }
            else if (protectedActive.Count > 0)
            {
                protectedBanner.Text = "보호 중 · " + string.Join(" · ", protectedActive.ToArray()) + " — 안전 정상화는 이 프로그램들을 종료하지 않습니다.";
                protectedBanner.BackColor = Palette.YellowLight;
            }
            else
            {
                protectedBanner.Text = "보호 대상 작업이 현재 실행 중이 아닙니다. 안전 정상화는 사용자 파일을 삭제하지 않습니다.";
                protectedBanner.BackColor = Palette.TealLight;
            }
            protectedList.Text = "항상 보호: 문서 · Google Drive · Windows 보안 · CHKDSK · 브라우저 · Office / 전사 작업은 10분 유휴 자동관리";
            cpuHotspotLabel.Text = lastHotspot.Available
                ? "실시간 CPU 상위 · " + lastHotspot.Summary
                : "실시간 CPU 상위를 측정하고 있습니다.";

            if (oneDriveRunaway)
                diagnosis.Text = "OneDrive가 " + s.TopHandleCount.ToString("N0") + "개 자원을 점유했습니다. 안전 정상화 실행을 권장합니다.";
            else if (sttIdleManaged && s.CpuPercent >= 90)
                diagnosis.Text = "전사 자동관리 상태: " + sttIdleText + ". 입력이 감지되면 현재 짧은 구간을 마친 뒤 CPU 사용을 멈춥니다.";
            else if (s.CpuPercent >= 90 && lastHotspot.Available)
                diagnosis.Text = "CPU가 " + s.CpuPercent.ToString("0") + "%입니다. 상위 점유 작업을 확인해 안전 정상화를 실행하세요.";
            else if (s.Maxim.Handles >= 10000 && !s.Maxim.Verified)
                diagnosis.Text = "오디오 프로그램 자원이 " + s.Maxim.Handles.ToString("N0") + "개로 높지만 대상을 확실히 검증하지 못해 경고만 표시합니다.";
            else if (s.Maxim.Handles >= 10000 && maximStreak < 2)
                diagnosis.Text = "오디오 프로그램 자원 누적 오류 가능성을 1차 확인했습니다. 다음 측정까지 자동 조치를 보류합니다.";
            else if (s.Maxim.Handles >= 10000 && maximStreak >= 2)
                diagnosis.Text = "오디오 프로그램 자원 누적 오류를 2회 연속 확인했습니다. 안전 점검·조치로 검증된 서비스만 재시작할 수 있습니다.";
            else if (s.CommitPercent >= 75)
                diagnosis.Text = "메모리 한도가 " + s.CommitPercent.ToString("0") + "%입니다. 사용하지 않는 큰 프로그램을 직접 닫아 여유를 확보하세요.";
            else if (driveLevel != HealthLevel.Normal)
                diagnosis.Text = "C: 드라이브 여유가 " + s.SystemDriveFreeGb.ToString("0.0") + "GB (" + s.SystemDriveFreePercent.ToString("0") + "%)입니다. 자동 삭제는 하지 않으며 직접 확인이 필요합니다.";
            else
                diagnosis.Text = "현재 주요 자원이 안정적입니다. 가장 많은 핸들을 쓰는 프로그램: " + s.TopHandleProcess + " (" + s.TopHandleCount.ToString("N0") + "개).";
        }

        private void NotifyIfNeeded(Snapshot snapshot, HealthEvaluation evaluation)
        {
            HealthLevel overall = evaluation.Overall;
            if (captureMode || !alertCheck.Checked || overall == HealthLevel.Normal) return;
            if (DateTime.Now - lastAlert < TimeSpan.FromMinutes(10)) return;
            lastAlert = DateTime.Now;
            string title = overall == HealthLevel.Warning ? "PC 자원 경고" : "PC 자원 주의";
            string text = SnapshotRules.ShortReason(snapshot, maximStreak, evaluation);
            tray.ShowBalloonTip(5000, title, text, overall == HealthLevel.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
            AppendLog(title + ": " + text);
        }

        private void OfficeCare(object sender, EventArgs e)
        {
            if (busy) return;
            string message =
                OfficeCareEngine.BuildPlan() +
                "\r\n\r\n사용자 파일은 삭제하지 않습니다. 탐색기 재시작 시 열려 있는 폴더 창만 닫힐 수 있습니다.\r\n계속할까요?";
            if (MessageBox.Show(message, "PC 안전 정상화", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                bool pauseStt = false;
                if (OfficeCareEngine.IsSttTranscriptionActive() && !OfficeCareEngine.IsSttIdlePolicyCurrent())
                {
                    DialogResult sttChoice = MessageBox.Show(
                        "법률 녹취 자동전사(STT)가 실행 중입니다. 이 작업은 CPU를 많이 사용하지만 중요한 작업이라 자동 종료하지 않습니다.\r\n\r\n예: 이번에는 일시중지 — 원본과 완료된 전사본은 보존되고 다음 로그인 때 이어받음\r\n아니요: 계속 보호 — CPU 사용량이 높게 유지될 수 있음",
                        "중요 작업 확인",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);
                    pauseStt = sttChoice == DialogResult.Yes;
                }
                RunOfficeCare(pauseStt);
            }
        }

        private async void RunOfficeCare(bool pauseStt)
        {
            busy = true;
            AppendLog("PC 안전 정상화 시작");
            string resultFile = Path.Combine(Path.GetTempPath(), "pc_care_v2_" + Guid.NewGuid().ToString("N") + ".txt");
            ElevatedOutcome outcome = null;
            try
            {
                outcome = await Task.Run(delegate { return ExecuteElevatedOfficeCare(resultFile, pauseStt); });
                if (outcome.Cancelled)
                {
                    AppendLog("관리자 권한 요청이 취소되어 아무것도 변경하지 않았습니다.");
                    return;
                }
                if (!string.IsNullOrEmpty(outcome.Error))
                {
                    AppendLog("PC 안전 정상화 실패: " + outcome.Error);
                    return;
                }

                string userResult = await Task.Run(delegate { return OfficeCareEngine.RunUserStage(); });
                string finalSummary = await Task.Run(delegate { return OfficeCareEngine.CaptureSummary("최종 확인"); });
                string combined =
                    (string.IsNullOrEmpty(outcome.Result) ? "[관리자 조치] 결과 없음" : outcome.Result) + Environment.NewLine +
                    userResult + Environment.NewLine +
                    finalSummary;
                AppendLog(combined.Replace(Environment.NewLine, " | "));
                AppLog.Write("PC 안전 정상화 결과 | " + combined.Replace("\r", " ").Replace("\n", " | "));
                MessageBox.Show(combined, "PC 안전 정상화 결과", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog("PC 안전 정상화 실패: " + ex.Message);
            }
            finally
            {
                try { if ((outcome == null || !outcome.StillRunning) && File.Exists(resultFile)) File.Delete(resultFile); } catch { }
                busy = false;
                RefreshStatus(true);
            }
        }

        private ElevatedOutcome ExecuteElevatedOfficeCare(string resultFile, bool pauseStt)
        {
            ElevatedOutcome outcome = new ElevatedOutcome();
            try
            {
                string arguments = "--elevated-office-care \"" + resultFile + "\"" + (pauseStt ? " --pause-stt" : "");
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, arguments);
                start.UseShellExecute = true;
                start.Verb = "runas";
                Process process = Process.Start(start);
                if (process != null)
                {
                    if (!process.WaitForExit(90000))
                    {
                        outcome.StillRunning = true;
                        outcome.Error = "관리자 조치가 90초 안에 끝나지 않았습니다. 강제 종료하지 않았습니다.";
                    }
                    else if (process.ExitCode != 0) outcome.Error = "관리자 조치 프로세스가 오류 코드 " + process.ExitCode + "로 종료됐습니다.";
                    process.Dispose();
                }
                if (!outcome.StillRunning && string.IsNullOrEmpty(outcome.Error) && File.Exists(resultFile))
                    outcome.Result = File.ReadAllText(resultFile, Encoding.UTF8).Trim();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) outcome.Cancelled = true;
                else outcome.Error = ex.Message;
            }
            catch (Exception ex) { outcome.Error = ex.Message; }
            return outcome;
        }

        private void SafeClean(object sender, EventArgs e)
        {
            if (busy) return;
            string message =
                "현재 조치할 항목이 없으면 사용량 수치가 거의 변하지 않을 수 있습니다.\r\n\r\n" +
                "확실하게 검증된 항목만 안전하게 조치합니다.\r\n" +
                "· 사용자 파일은 삭제하지 않음\r\n" +
                "· OneDrive·Google Drive·백신·CHKDSK·브라우저·Office는 종료하지 않음\r\n" +
                "· Robocopy는 종료하지 않고 우선순위만 낮춤\r\n" +
                "· Maxim은 10,000개 이상을 2회 연속 확인하고 서비스까지 검증된 경우만 재시작\r\n" +
                "· 불확실한 항목은 경고와 기록만 남김\r\n\r\n" +
                "계속할까요?";
            if (MessageBox.Show(message, "안전 점검·조치", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                RunElevatedSafeClean();
        }

        private async void RunElevatedSafeClean()
        {
            busy = true;
            AppendLog("안전 점검·조치 시작");
            string resultFile = Path.Combine(Path.GetTempPath(), "pc_care_v2_" + Guid.NewGuid().ToString("N") + ".txt");
            ElevatedOutcome outcome = null;
            try
            {
                outcome = await Task.Run(delegate { return ExecuteElevatedSafeClean(resultFile); });
                if (outcome.Cancelled)
                {
                    AppendLog("관리자 권한 요청이 취소됐습니다.");
                }
                else if (!string.IsNullOrEmpty(outcome.Error)) AppendLog("안전 점검·조치 실패: " + outcome.Error);
                else if (!string.IsNullOrEmpty(outcome.Result))
                {
                    AppendLog(outcome.Result.Replace(Environment.NewLine, " | "));
                    MessageBox.Show(outcome.Result, "안전 점검·조치 결과", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else AppendLog("안전 점검·조치 결과 파일을 받지 못했습니다.");
            }
            catch (Exception ex) { AppendLog("안전 점검·조치 실패: " + ex.Message); }
            finally
            {
                try { if ((outcome == null || !outcome.StillRunning) && File.Exists(resultFile)) File.Delete(resultFile); } catch { }
                busy = false;
                RefreshStatus(true);
            }
        }

        private ElevatedOutcome ExecuteElevatedSafeClean(string resultFile)
        {
            ElevatedOutcome outcome = new ElevatedOutcome();
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--elevated-safe-clean \"" + resultFile + "\"");
                start.UseShellExecute = true;
                start.Verb = "runas";
                Process process = Process.Start(start);
                if (process != null)
                {
                    if (!process.WaitForExit(90000))
                    {
                        outcome.StillRunning = true;
                        outcome.Error = "안전 점검·조치가 90초 안에 끝나지 않았습니다. 작업은 강제 종료하지 않았으며 임시 결과 기록을 보존했습니다.";
                    }
                    else if (process.ExitCode != 0) outcome.Error = "관리자 안전 점검·조치 프로세스가 오류 코드 " + process.ExitCode + "로 종료됐습니다.";
                    process.Dispose();
                }
                if (!outcome.StillRunning && string.IsNullOrEmpty(outcome.Error) && File.Exists(resultFile)) outcome.Result = File.ReadAllText(resultFile, Encoding.UTF8).Trim();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) outcome.Cancelled = true;
                else outcome.Error = ex.Message;
            }
            catch (Exception ex) { outcome.Error = ex.Message; }
            return outcome;
        }

        private void CopyStatus(object sender, EventArgs e)
        {
            Snapshot snapshot = Snapshot.Capture(cpuSampler.Sample(), diskSampler.Sample());
            Clipboard.SetText(snapshot.ToReport());
            AppendLog("현재 상태를 클립보드에 복사했습니다.");
        }

        private void OpenLogFolder(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Branding.DataDirectory);
                Process.Start(Branding.DataDirectory);
            }
            catch (Exception ex) { AppendLog("기록 폴더 열기 실패: " + ex.Message); }
        }

        private async void SaveDiagnosticReport(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "PC 진단 보고서 저장";
                dialog.Filter = "텍스트 문서 (*.txt)|*.txt";
                dialog.FileName = "더원_PC진단_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyMMdd_HHmm") + ".txt";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string report = await Task.Run(delegate
                    {
                        return Branding.ProductName + " v" + Branding.Version + Environment.NewLine +
                            "컴퓨터: " + Environment.MachineName + Environment.NewLine +
                            "프로필: " + ProfileDetector.DisplayName + Environment.NewLine +
                            "생성 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + Environment.NewLine +
                            OfficeCareEngine.CaptureSummary("현재 상태") + Environment.NewLine + Environment.NewLine +
                            OfficeCareEngine.BuildPlan();
                    });
                    File.WriteAllText(dialog.FileName, report, new UTF8Encoding(true));
                    AppendLog("진단 보고서를 저장했습니다: " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("진단 보고서를 저장하지 못했습니다.\r\n" + ex.Message, "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ResetWidgetPosition(object sender, EventArgs e)
        {
            WidgetPosition.Reset();
            if (widget != null && !widget.IsDisposed) widget.PlaceAtDefault();
            AppendLog("상시 위젯 위치를 화면 오른쪽 위로 초기화했습니다.");
        }

        private void ToggleStartup()
        {
            try
            {
                StartupManager.SetEnabled(startupCheck.Checked);
                AppendLog(startupCheck.Checked ? "Windows 시작 시 실행을 켰습니다." : "Windows 시작 시 실행을 껐습니다.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("시작 설정을 바꾸지 못했습니다.\r\n" + ex.Message, "설정 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AppendLog(string message)
        {
            string clean = message.Replace("\r", " ").Replace("\n", " ").Trim();
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + clean;
            if (logBox.TextLength > 26000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 18000);
            logBox.AppendText((logBox.TextLength == 0 ? "" : Environment.NewLine) + line);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
            AppLog.Write(line);
        }
    }

    internal sealed class WidgetMetric
    {
        private readonly Panel panel;
        private readonly Label value;
        private readonly Label caption;
        private readonly string titleText;

        public WidgetMetric(TableLayoutPanel host, int column, string title)
        {
            titleText = title;
            panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(column == 0 ? 0 : 3, 0, column == 5 ? 0 : 3, 0),
                BackColor = Palette.UnknownLight
            };
            caption = new Label
            {
                Text = title + Environment.NewLine + "--",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Malgun Gothic", 8.4f, FontStyle.Bold),
                ForeColor = Palette.Gray
            };
            value = new Label
            {
                Text = "--",
                AutoSize = false,
                Location = new Point(0, 24),
                Size = new Size(100, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Malgun Gothic", 11f, FontStyle.Bold),
                ForeColor = Palette.Charcoal,
                Visible = false
            };
            panel.Controls.Add(value);
            panel.Controls.Add(caption);
            host.Controls.Add(panel, column, 0);
        }

        public void Apply(string display, HealthLevel level)
        {
            value.Text = display;
            caption.Text = titleText + Environment.NewLine + display;
            panel.BackColor = Palette.Surface(level);
            caption.ForeColor = Palette.Accent(level);
        }
    }

    internal sealed class WidgetForm : Form
    {
        private readonly Action openDashboard;
        private readonly Action safeClean;
        private readonly Action hideWidget;
        private readonly Action exitApplication;
        private Icon widgetIcon;
        private readonly Label status;
        private readonly Label message;
        private readonly WidgetMetric cpu;
        private readonly WidgetMetric ram;
        private readonly WidgetMetric commit;
        private readonly WidgetMetric disk;
        private readonly WidgetMetric handles;
        private readonly WidgetMetric drive;
        private bool placed;

        public WidgetForm(Action openAction, Action cleanAction, Action hideAction, Action exitAction)
        {
            openDashboard = openAction;
            safeClean = cleanAction;
            hideWidget = hideAction;
            exitApplication = exitAction;
            Text = Branding.ProductName + " 상시 위젯";
            Font = new Font("Malgun Gothic", 9f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Palette.White;
            Size = new Size(760, 174);
            MinimumSize = Size;
            MaximumSize = Size;
            widgetIcon = Branding.CreateIcon();
            Icon = widgetIcon;
            Opacity = .98;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                BackColor = Palette.White,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            Panel header = new Panel { Dock = DockStyle.Fill, BackColor = Palette.White, Cursor = Cursors.SizeAll };
            Label product = new Label
            {
                Text = Branding.ProductName,
                AutoSize = true,
                Location = new Point(2, 4),
                Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold),
                ForeColor = Palette.Charcoal
            };
            status = new Label
            {
                Text = "측정 중",
                AutoSize = false,
                Size = new Size(110, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Palette.Unknown,
                ForeColor = Palette.White,
                Font = new Font("Malgun Gothic", 8.4f, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(562, 1)
            };
            Button hide = WidgetHeaderButton("—", "위젯 숨기기", Palette.Gray, delegate { hideWidget(); });
            Button close = WidgetHeaderButton("×", "프로그램 종료", Palette.Red, delegate { exitApplication(); });
            header.Controls.Add(product);
            header.Controls.Add(status);
            header.Controls.Add(hide);
            header.Controls.Add(close);
            header.Resize += delegate
            {
                close.Left = Math.Max(0, header.ClientSize.Width - close.Width);
                hide.Left = Math.Max(0, close.Left - hide.Width - 4);
                status.Left = Math.Max(0, hide.Left - status.Width - 8);
            };
            header.MouseDown += BeginDrag;
            product.MouseDown += BeginDrag;
            product.DoubleClick += delegate { openDashboard(); };
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
            for (int i = 0; i < 6; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.6667f));
            cpu = new WidgetMetric(metrics, 0, "CPU");
            ram = new WidgetMetric(metrics, 1, "RAM");
            commit = new WidgetMetric(metrics, 2, "메모리 한도");
            disk = new WidgetMetric(metrics, 3, "디스크");
            handles = new WidgetMetric(metrics, 4, "시스템 자원");
            drive = new WidgetMetric(metrics, 5, "C: 여유");
            root.Controls.Add(metrics, 0, 1);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 5, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            message = new Label
            {
                Text = "주요 자원을 확인하고 있습니다.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = Palette.Gray,
                Padding = new Padding(3, 0, 3, 0)
            };
            Button clean = WidgetButton("정상화", Palette.Teal, delegate { safeClean(); });
            Button open = WidgetButton("대시보드", Palette.Charcoal, delegate { openDashboard(); });
            footer.Controls.Add(message, 0, 0);
            footer.Controls.Add(clean, 1, 0);
            footer.Controls.Add(open, 2, 0);
            root.Controls.Add(footer, 0, 2);

            Shown += delegate
            {
                if (!placed)
                {
                    placed = true;
                    Point? saved = WidgetPosition.Load();
                    if (saved.HasValue && IsVisibleLocation(saved.Value)) Location = saved.Value;
                    else PlaceAtDefault();
                }
            };
            Move += delegate { if (placed && WindowState == FormWindowState.Normal) WidgetPosition.Save(Location); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CS_DROPSHADOW;
                return parameters;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && widgetIcon != null)
            {
                widgetIcon.Dispose();
                widgetIcon = null;
            }
        }

        private Button WidgetButton(string text, Color color, EventHandler handler)
        {
            Button button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Palette.White,
                Font = new Font("Malgun Gothic", 8.4f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += handler;
            return button;
        }

        private Button WidgetHeaderButton(string text, string accessibleName, Color color, EventHandler handler)
        {
            Button button = new RoundedButton
            {
                Text = text,
                AccessibleName = accessibleName,
                Size = new Size(28, 24),
                Top = 1,
                FlatStyle = FlatStyle.Flat,
                BackColor = Palette.White,
                ForeColor = color,
                Font = new Font("Malgun Gothic", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += handler;
            return button;
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        public void PlaceAtDefault()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(Math.Max(area.Left, area.Right - Width - 18), area.Top + 18);
        }

        private bool IsVisibleLocation(Point point)
        {
            Rectangle candidate = new Rectangle(point, Size);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(candidate)) return true;
            }
            return false;
        }

        public void UpdateSnapshot(Snapshot snapshot, HealthEvaluation evaluation, int maximStreak)
        {
            cpu.Apply(snapshot.CpuPercent.ToString("0") + "%", evaluation.Cpu);
            ram.Apply(snapshot.MemoryUsedPercent.ToString("0") + "%", evaluation.Ram);
            commit.Apply(snapshot.CommitPercent.ToString("0") + "%", SnapshotRules.HighPercent(snapshot.CommitPercent, 75, 90));
            disk.Apply(snapshot.DiskAvailable ? snapshot.DiskPercent.ToString("0") + "%" : "--", evaluation.Disk);
            handles.Apply(snapshot.HandleCount.ToString("N0"), SnapshotRules.HighLong(snapshot.HandleCount, 250000, 500000));
            drive.Apply(snapshot.SystemDriveFreeGb.ToString("0.0") + "GB", SnapshotRules.DriveFree(snapshot.SystemDriveFreeGb, snapshot.SystemDriveFreePercent));
            status.Text = evaluation.Overall == HealthLevel.Warning ? "경고" : evaluation.Overall == HealthLevel.Caution ? "주의" : "정상";
            status.BackColor = Palette.Accent(evaluation.Overall);
            message.Text = SnapshotRules.ShortReason(snapshot, maximStreak, evaluation);
        }
    }

    internal static class WidgetPreference
    {
        private static string FilePath { get { return Path.Combine(Branding.DataDirectory, "widget-visible.txt"); } }

        public static bool Load()
        {
            try
            {
                bool visible;
                if (bool.TryParse(File.ReadAllText(FilePath, Encoding.UTF8).Trim(), out visible)) return visible;
            }
            catch { }
            return true;
        }

        public static void Save(bool visible)
        {
            try
            {
                Directory.CreateDirectory(Branding.DataDirectory);
                File.WriteAllText(FilePath, visible.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class WidgetPosition
    {
        private static string FilePath { get { return Path.Combine(Branding.DataDirectory, "widget-position.txt"); } }

        public static Point? Load()
        {
            try
            {
                string[] parts = File.ReadAllText(FilePath, Encoding.UTF8).Trim().Split('|');
                if (parts.Length == 2) return new Point(int.Parse(parts[0]), int.Parse(parts[1]));
            }
            catch { }
            return null;
        }

        public static void Save(Point point)
        {
            try
            {
                Directory.CreateDirectory(Branding.DataDirectory);
                File.WriteAllText(FilePath, point.X + "|" + point.Y, Encoding.UTF8);
            }
            catch { }
        }

        public static void Reset()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }

    internal sealed class SustainedMeter
    {
        private readonly int cautionRequired;
        private readonly int warningRequired;
        private readonly int releaseRequired;
        private HealthLevel current = HealthLevel.Normal;
        private int cautionRun;
        private int warningRun;
        private int belowWarningRun;
        private int belowCautionRun;

        public SustainedMeter(int cautionSamples, int warningSamples, int releaseSamples)
        {
            cautionRequired = Math.Max(1, cautionSamples);
            warningRequired = Math.Max(cautionRequired, warningSamples);
            releaseRequired = Math.Max(1, releaseSamples);
        }

        public HealthLevel Update(double value, double caution, double warning)
        {
            if (value >= caution) cautionRun++; else cautionRun = 0;
            if (value >= warning) warningRun++; else warningRun = 0;
            if (value < warning - 8) belowWarningRun++; else belowWarningRun = 0;
            if (value < caution - 5) belowCautionRun++; else belowCautionRun = 0;

            if (current == HealthLevel.Warning)
            {
                if (belowCautionRun >= releaseRequired) current = HealthLevel.Normal;
                else if (belowWarningRun >= releaseRequired) current = HealthLevel.Caution;
            }
            else if (current == HealthLevel.Caution)
            {
                if (warningRun >= warningRequired) current = HealthLevel.Warning;
                else if (belowCautionRun >= releaseRequired) current = HealthLevel.Normal;
            }
            else
            {
                if (warningRun >= warningRequired) current = HealthLevel.Warning;
                else if (cautionRun >= cautionRequired) current = HealthLevel.Caution;
            }
            return current;
        }
    }

    internal sealed class HealthEvaluation
    {
        public HealthLevel Cpu;
        public HealthLevel Ram;
        public HealthLevel Disk;
        public HealthLevel Overall;

        public static HealthEvaluation Immediate(Snapshot snapshot, int maximStreak)
        {
            HealthEvaluation result = new HealthEvaluation();
            result.Cpu = SnapshotRules.HighPercent(snapshot.CpuPercent, 70, 90);
            result.Ram = SnapshotRules.HighPercent(snapshot.MemoryUsedPercent, 80, 90);
            result.Disk = snapshot.DiskAvailable ? SnapshotRules.HighPercent(snapshot.DiskPercent, 80, 95) : HealthLevel.Unknown;
            result.Overall = SnapshotRules.OverallFromLevels(snapshot, maximStreak, result.Cpu, result.Ram, result.Disk);
            return result;
        }
    }

    internal sealed class SustainedHealth
    {
        private readonly SustainedMeter cpu = new SustainedMeter(10, 20, 20);
        private readonly SustainedMeter ram = new SustainedMeter(2, 10, 20);
        private readonly SustainedMeter disk = new SustainedMeter(20, 40, 20);

        public HealthEvaluation Update(Snapshot snapshot, int maximStreak)
        {
            HealthEvaluation result = new HealthEvaluation();
            result.Cpu = cpu.Update(snapshot.CpuPercent, 70, 90);
            result.Ram = ram.Update(snapshot.MemoryUsedPercent, 80, 90);
            result.Disk = snapshot.DiskAvailable ? disk.Update(snapshot.DiskPercent, 80, 95) : HealthLevel.Unknown;
            result.Overall = SnapshotRules.OverallFromLevels(snapshot, maximStreak, result.Cpu, result.Ram, result.Disk);
            return result;
        }
    }

    internal static class SnapshotRules
    {
        public static HealthLevel HighPercent(double value, double caution, double warning)
        {
            if (value >= warning) return HealthLevel.Warning;
            if (value >= caution) return HealthLevel.Caution;
            return HealthLevel.Normal;
        }

        public static HealthLevel LowPercent(double value, double caution, double warning)
        {
            if (value <= warning) return HealthLevel.Warning;
            if (value <= caution) return HealthLevel.Caution;
            return HealthLevel.Normal;
        }

        public static HealthLevel DriveFree(double freeGb, double freePercent)
        {
            if (freeGb < 5 || (freeGb < 10 && freePercent < 8)) return HealthLevel.Warning;
            if (freeGb < 20 && freePercent < 15) return HealthLevel.Caution;
            return HealthLevel.Normal;
        }

        public static HealthLevel HighLong(long value, long caution, long warning)
        {
            if (value >= warning) return HealthLevel.Warning;
            if (value >= caution) return HealthLevel.Caution;
            return HealthLevel.Normal;
        }

        public static HealthLevel Overall(Snapshot snapshot, int maximStreak)
        {
            HealthLevel cpu = HighPercent(snapshot.CpuPercent, 70, 90);
            HealthLevel ram = HighPercent(snapshot.MemoryUsedPercent, 80, 90);
            HealthLevel disk = snapshot.DiskAvailable ? HighPercent(snapshot.DiskPercent, 80, 95) : HealthLevel.Unknown;
            return OverallFromLevels(snapshot, maximStreak, cpu, ram, disk);
        }

        public static HealthLevel OverallFromLevels(Snapshot snapshot, int maximStreak, HealthLevel cpu, HealthLevel ram, HealthLevel disk)
        {
            HealthLevel level = HealthLevel.Normal;
            level = Max(level, cpu);
            level = Max(level, ram);
            level = Max(level, HighPercent(snapshot.CommitPercent, 75, 90));
            if (snapshot.DiskAvailable) level = Max(level, disk);
            level = Max(level, HighLong(snapshot.HandleCount, 250000, 500000));
            level = Max(level, DriveFree(snapshot.SystemDriveFreeGb, snapshot.SystemDriveFreePercent));
            if (snapshot.Maxim.Handles >= 10000)
            {
                if (!snapshot.Maxim.Verified || maximStreak < 2) level = Max(level, HealthLevel.Caution);
                else level = Max(level, HealthLevel.Warning);
            }
            return level;
        }

        private static HealthLevel Max(HealthLevel left, HealthLevel right)
        {
            return (int)left >= (int)right ? left : right;
        }

        public static string ShortReason(Snapshot snapshot, int maximStreak, HealthEvaluation evaluation)
        {
            if (snapshot.Maxim.Handles >= 10000 && (!snapshot.Maxim.Verified || maximStreak < 2))
                return "오디오 프로그램 자원이 높아 확인 중입니다. 불확실한 항목은 자동 조치하지 않습니다.";
            if (snapshot.Maxim.Handles >= 10000 && maximStreak >= 2)
                return "검증된 오디오 프로그램 자원 누적 오류가 확인됐습니다.";
            if (evaluation.Cpu == HealthLevel.Warning) return "CPU 사용량이 경고 범위로 60초 이상 지속됐습니다.";
            if (snapshot.CommitPercent >= 90) return "메모리 한도가 90% 이상입니다.";
            if (evaluation.Ram == HealthLevel.Warning) return "RAM 사용량이 경고 범위로 30초 이상 지속됐습니다.";
            if (evaluation.Disk == HealthLevel.Warning) return "디스크 활동이 경고 범위로 120초 이상 지속됐습니다.";
            HealthLevel drive = DriveFree(snapshot.SystemDriveFreeGb, snapshot.SystemDriveFreePercent);
            if (drive == HealthLevel.Warning) return "C: 드라이브 여유가 " + snapshot.SystemDriveFreeGb.ToString("0.0") + "GB입니다. 자동 삭제하지 않습니다.";
            if (snapshot.HandleCount >= 500000) return "Windows 자원이 비정상적으로 많이 쌓였습니다.";
            if (snapshot.CommitPercent >= 75 || evaluation.Ram == HealthLevel.Caution || evaluation.Cpu == HealthLevel.Caution || evaluation.Disk == HealthLevel.Caution)
                return "일부 자원이 주의 범위입니다. 대시보드에서 확인하세요.";
            if (drive == HealthLevel.Caution) return "C: 드라이브 여유가 주의 범위입니다.";
            return "주요 자원이 안정적입니다.";
        }
    }

    internal sealed class Snapshot
    {
        public double CpuPercent;
        public double MemoryUsedPercent;
        public double CommitPercent;
        public double AvailableGb;
        public double DiskPercent;
        public bool DiskAvailable;
        public double SystemDriveFreePercent;
        public double SystemDriveFreeGb;
        public long HandleCount;
        public int ProcessCount;
        public string TopHandleProcess = "확인 불가";
        public long TopHandleCount;
        public bool RobocopyActive;
        public bool ChkdskActive;
        public bool OneDriveActive;
        public bool GoogleDriveActive;
        public bool DefenderActive;
        public bool OfficeActive;
        public bool BrowserActive;
        public MaximInfo Maxim = new MaximInfo();

        public static Snapshot CreateForUiState(string state)
        {
            Snapshot snapshot = new Snapshot();
            snapshot.DiskAvailable = true;
            snapshot.ProcessCount = 196;
            snapshot.TopHandleProcess = "ExampleApp";
            snapshot.TopHandleCount = 4200;
            snapshot.AvailableGb = 8.4;
            snapshot.OneDriveActive = true;
            snapshot.DefenderActive = true;
            if (string.Equals(state, "warning", StringComparison.OrdinalIgnoreCase))
            {
                snapshot.TopHandleProcess = "OneDrive";
                snapshot.TopHandleCount = 1810000;
                snapshot.CpuPercent = 94;
                snapshot.MemoryUsedPercent = 93;
                snapshot.CommitPercent = 92;
                snapshot.DiskPercent = 97;
                snapshot.HandleCount = 2900000;
                snapshot.SystemDriveFreeGb = 4.2;
                snapshot.SystemDriveFreePercent = 6;
            }
            else if (string.Equals(state, "caution", StringComparison.OrdinalIgnoreCase))
            {
                snapshot.CpuPercent = 78;
                snapshot.MemoryUsedPercent = 81;
                snapshot.CommitPercent = 79;
                snapshot.DiskPercent = 86;
                snapshot.HandleCount = 310000;
                snapshot.SystemDriveFreeGb = 14.5;
                snapshot.SystemDriveFreePercent = 12;
            }
            else
            {
                snapshot.CpuPercent = 32;
                snapshot.MemoryUsedPercent = 48;
                snapshot.CommitPercent = 52;
                snapshot.DiskPercent = 18;
                snapshot.HandleCount = 145000;
                snapshot.SystemDriveFreeGb = 105;
                snapshot.SystemDriveFreePercent = 11;
            }
            return snapshot;
        }

        public static Snapshot Capture(double cpu, DiskReading disk)
        {
            Snapshot snapshot = new Snapshot();
            snapshot.CpuPercent = Math.Max(0, Math.Min(100, cpu));
            snapshot.DiskAvailable = disk.Available;
            snapshot.DiskPercent = Math.Max(0, Math.Min(100, disk.Percent));

            NativeMethods.PERFORMANCE_INFORMATION information = new NativeMethods.PERFORMANCE_INFORMATION();
            information.cb = Marshal.SizeOf(typeof(NativeMethods.PERFORMANCE_INFORMATION));
            if (NativeMethods.GetPerformanceInfo(out information, information.cb))
            {
                double page = information.PageSize.ToUInt64();
                double total = information.PhysicalTotal.ToUInt64() * page;
                double available = information.PhysicalAvailable.ToUInt64() * page;
                snapshot.MemoryUsedPercent = total > 0 ? ((total - available) / total) * 100.0 : 0;
                snapshot.AvailableGb = available / 1073741824.0;
                double commitLimit = information.CommitLimit.ToUInt64();
                snapshot.CommitPercent = commitLimit > 0 ? (information.CommitTotal.ToUInt64() / commitLimit) * 100.0 : 0;
                snapshot.HandleCount = information.HandleCount;
                snapshot.ProcessCount = (int)information.ProcessCount;
            }

            try
            {
                string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
                DriveInfo drive = new DriveInfo(systemRoot);
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    snapshot.SystemDriveFreeGb = drive.AvailableFreeSpace / 1073741824.0;
                    snapshot.SystemDriveFreePercent = drive.AvailableFreeSpace * 100.0 / drive.TotalSize;
                }
            }
            catch { snapshot.SystemDriveFreePercent = 100; }

            List<int> maximProcessIds = new List<int>();
            List<string> maximPaths = new List<string>();
            bool maximPathsVerified = true;
            Process[] all = Process.GetProcesses();
            foreach (Process process in all)
            {
                try
                {
                    string name = process.ProcessName;
                    string lower = name.ToLowerInvariant();
                    long handles = process.HandleCount;
                    if (handles > snapshot.TopHandleCount)
                    {
                        snapshot.TopHandleCount = handles;
                        snapshot.TopHandleProcess = name;
                    }
                    if (lower == "robocopy") snapshot.RobocopyActive = true;
                    else if (lower == "chkdsk") snapshot.ChkdskActive = true;
                    else if (lower == "onedrive" || lower == "onedrive.sync.service") snapshot.OneDriveActive = true;
                    else if (lower == "googledrivefs") snapshot.GoogleDriveActive = true;
                    else if (lower == "msmpeng") snapshot.DefenderActive = true;
                    else if (lower == "winword" || lower == "excel" || lower == "powerpnt" || lower == "outlook") snapshot.OfficeActive = true;
                    else if (lower == "chrome" || lower == "msedge" || lower == "firefox") snapshot.BrowserActive = true;

                    if (string.Equals(name, "MaximAudioService64", StringComparison.OrdinalIgnoreCase))
                    {
                        snapshot.Maxim.Handles += handles;
                        maximProcessIds.Add(process.Id);
                        string path = NativeMethods.TryGetProcessPath(process);
                        maximPaths.Add(path);
                        if (string.IsNullOrEmpty(path) ||
                            !File.Exists(path) ||
                            !string.Equals(Path.GetFileName(path), "MaximAudioService64.exe", StringComparison.OrdinalIgnoreCase) ||
                            !IsTrustedExecutablePath(path))
                            maximPathsVerified = false;
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }

            snapshot.Maxim.ProcessCount = maximProcessIds.Count;
            if (maximProcessIds.Count > 0)
            {
                MaximServiceVerification verification = VerifyMaximService(maximProcessIds);
                snapshot.Maxim.ServiceName = verification.ServiceName;
                snapshot.Maxim.Verified = maximPathsVerified && verification.Verified;
                snapshot.Maxim.Reason = snapshot.Maxim.Verified ? "실행 파일과 Windows 서비스 확인" : verification.Reason;
                snapshot.Maxim.Path = maximPaths.FirstOrDefault(delegate(string x) { return !string.IsNullOrEmpty(x); }) ?? "";
            }
            else
            {
                snapshot.Maxim.Verified = false;
                snapshot.Maxim.Reason = "대상 프로세스 없음";
            }
            return snapshot;
        }

        private static MaximServiceVerification VerifyMaximService(List<int> processIds)
        {
            MaximServiceVerification result = new MaximServiceVerification();
            try
            {
                int serviceProcessId = NativeMethods.TryGetServiceProcessId("MaximAudioService");
                if (serviceProcessId > 0 && processIds.Contains(serviceProcessId))
                {
                    result.Verified = true;
                    result.ServiceName = "MaximAudioService";
                    result.Reason = "Windows 서비스 PID 확인";
                }
                else if (serviceProcessId <= 0) result.Reason = "MaximAudioService의 실행 PID를 확인하지 못함";
                else result.Reason = "MaximAudioService PID와 대상 프로세스가 일치하지 않음";
            }
            catch (Exception ex)
            {
                result.Reason = "서비스 검증 실패: " + ex.GetType().Name;
            }
            return result;
        }

        private static bool IsTrustedExecutablePath(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string lower = full.ToLowerInvariant();
                string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).ToLowerInvariant();
                string temp = Path.GetTempPath().ToLowerInvariant();
                if ((!string.IsNullOrEmpty(user) && lower.StartsWith(user)) || (!string.IsNullOrEmpty(temp) && lower.StartsWith(temp))) return false;
                foreach (string root in TrustedRoots())
                {
                    if (!string.IsNullOrEmpty(root) && lower.StartsWith(root.ToLowerInvariant().TrimEnd('\\') + "\\")) return true;
                }
            }
            catch { }
            return false;
        }

        private static IEnumerable<string> TrustedRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        }

        public List<string> GetActiveProtectedNames()
        {
            List<string> names = new List<string>();
            if (OneDriveActive) names.Add("OneDrive");
            if (GoogleDriveActive) names.Add("Google Drive");
            if (DefenderActive) names.Add("Windows 보안");
            if (ChkdskActive) names.Add("CHKDSK");
            if (BrowserActive) names.Add("브라우저");
            if (OfficeActive) names.Add("Office");
            if (RobocopyActive) names.Add("Robocopy(종료 금지)");
            return names;
        }

        public string ToReport()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine(Branding.ProductName + " 상태 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("CPU: " + CpuPercent.ToString("0.0") + "%");
            report.AppendLine("RAM 사용: " + MemoryUsedPercent.ToString("0.0") + "% (가용 " + AvailableGb.ToString("0.0") + "GB)");
            report.AppendLine("메모리 커밋: " + CommitPercent.ToString("0.0") + "%");
            report.AppendLine("디스크 활동: " + (DiskAvailable ? DiskPercent.ToString("0.0") + "%" : "측정 지원 안 됨"));
            report.AppendLine("C: 여유: " + SystemDriveFreePercent.ToString("0.0") + "% (" + SystemDriveFreeGb.ToString("0.0") + "GB)");
            report.AppendLine("프로세스: " + ProcessCount.ToString("N0") + " / 시스템 핸들: " + HandleCount.ToString("N0"));
            report.AppendLine("최다 핸들: " + TopHandleProcess + " " + TopHandleCount.ToString("N0"));
            report.AppendLine("Maxim 오디오: " + Maxim.Handles.ToString("N0") + " handles / 검증 " + (Maxim.Verified ? "완료" : "안 됨") + " / " + Maxim.Reason);
            List<string> protectedNames = GetActiveProtectedNames();
            report.AppendLine("현재 보호 대상: " + (protectedNames.Count > 0 ? string.Join(", ", protectedNames.ToArray()) : "실행 중인 항목 없음"));
            report.AppendLine("PC 안전 정상화 원칙: 사용자 파일 삭제 없음 / OneDrive·탐색기는 임계값 이상일 때만 재시작 / 중요 작업은 별도 동의");
            return report.ToString();
        }
    }

    internal sealed class MaximInfo
    {
        public long Handles;
        public int ProcessCount;
        public bool Verified;
        public string ServiceName = "";
        public string Path = "";
        public string Reason = "";
    }

    internal sealed class MaximServiceVerification
    {
        public bool Verified;
        public string ServiceName = "";
        public string Reason = "";
    }

    internal sealed class CpuHotspot
    {
        public bool Available;
        public string Summary = "";
    }

    internal sealed class ProcessCpuSampler
    {
        private sealed class Point
        {
            public string Name = "";
            public double CpuSeconds;
        }

        private Dictionary<int, Point> previous = new Dictionary<int, Point>();
        private DateTime previousAt;

        public CpuHotspot Sample()
        {
            DateTime now = DateTime.UtcNow;
            Dictionary<int, Point> current = new Dictionary<int, Point>();
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    current[process.Id] = new Point
                    {
                        Name = NormalizeName(process.ProcessName),
                        CpuSeconds = process.TotalProcessorTime.TotalSeconds
                    };
                }
                catch { }
                finally { process.Dispose(); }
            }

            double elapsed = previousAt == default(DateTime) ? 0 : (now - previousAt).TotalSeconds;
            Dictionary<int, Point> before = previous;
            previous = current;
            previousAt = now;
            if (elapsed < 0.5 || before.Count == 0) return new CpuHotspot();

            Dictionary<string, double> grouped = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, Point> pair in current)
            {
                Point old;
                if (!before.TryGetValue(pair.Key, out old)) continue;
                double delta = pair.Value.CpuSeconds - old.CpuSeconds;
                if (delta <= 0) continue;
                double percent = delta / elapsed / Math.Max(1, Environment.ProcessorCount) * 100.0;
                double existing;
                grouped.TryGetValue(pair.Value.Name, out existing);
                grouped[pair.Value.Name] = existing + percent;
            }

            string[] top = grouped
                .Where(delegate(KeyValuePair<string, double> pair) { return pair.Value >= 0.3; })
                .OrderByDescending(delegate(KeyValuePair<string, double> pair) { return pair.Value; })
                .Take(3)
                .Select(delegate(KeyValuePair<string, double> pair) { return pair.Key + " " + pair.Value.ToString("0") + "%"; })
                .ToArray();
            return new CpuHotspot { Available = top.Length > 0, Summary = string.Join(" · ", top) };
        }

        private static string NormalizeName(string name)
        {
            if (string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase)) return "Windows 탐색기";
            if (string.Equals(name, "pythonw", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "python", StringComparison.OrdinalIgnoreCase)) return "Python/STT";
            if (name.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)) return "OneDrive";
            if (string.Equals(name, "chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
            if (string.Equals(name, "msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
            if (string.Equals(name, "System", StringComparison.OrdinalIgnoreCase)) return "Windows 시스템";
            return name;
        }
    }

    internal sealed class CpuSampler
    {
        private ulong previousIdle;
        private ulong previousKernel;
        private ulong previousUser;
        private bool ready;

        public double Sample()
        {
            NativeMethods.FILETIME idle;
            NativeMethods.FILETIME kernel;
            NativeMethods.FILETIME user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user)) return 0;
            ulong idleValue = NativeMethods.ToUInt64(idle);
            ulong kernelValue = NativeMethods.ToUInt64(kernel);
            ulong userValue = NativeMethods.ToUInt64(user);
            if (!ready)
            {
                previousIdle = idleValue;
                previousKernel = kernelValue;
                previousUser = userValue;
                ready = true;
                return 0;
            }
            ulong idleDelta = idleValue - previousIdle;
            ulong systemDelta = (kernelValue - previousKernel) + (userValue - previousUser);
            previousIdle = idleValue;
            previousKernel = kernelValue;
            previousUser = userValue;
            if (systemDelta == 0) return 0;
            double value = (systemDelta - idleDelta) * 100.0 / systemDelta;
            return Math.Max(0, Math.Min(100, value));
        }
    }

    internal struct DiskReading
    {
        public bool Available;
        public double Percent;
    }

    internal sealed class DiskSampler : IDisposable
    {
        private readonly object sync = new object();
        private PerformanceCounter counter;
        private Task<PerformanceCounter> initialization;
        private bool unavailable;
        private bool disposed;

        public DiskReading Sample()
        {
            DiskReading reading = new DiskReading();
            try
            {
                lock (sync)
                {
                    if (disposed || unavailable) return reading;
                    if (counter != null)
                    {
                        reading.Available = true;
                        reading.Percent = Math.Max(0, Math.Min(100, counter.NextValue()));
                        return reading;
                    }

                    if (initialization == null)
                    {
                        initialization = Task.Factory.StartNew<PerformanceCounter>(
                            CreateCounter,
                            CancellationToken.None,
                            TaskCreationOptions.DenyChildAttach,
                            TaskScheduler.Default);
                        return reading;
                    }

                    if (!initialization.IsCompleted) return reading;
                    if (initialization.IsCanceled || initialization.IsFaulted)
                    {
                        unavailable = true;
                        initialization = null;
                        return reading;
                    }

                    counter = initialization.Result;
                    initialization = null;
                    reading.Available = true;
                    reading.Percent = 0;
                }
            }
            catch
            {
                reading.Available = false;
                reading.Percent = 0;
            }
            return reading;
        }

        public void Dispose()
        {
            Task<PerformanceCounter> pending;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                if (counter != null)
                {
                    counter.Dispose();
                    counter = null;
                }
                pending = initialization;
                initialization = null;
            }
            if (pending != null)
            {
                pending.ContinueWith(delegate(Task<PerformanceCounter> task)
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result != null) task.Result.Dispose();
                }, TaskScheduler.Default);
            }
        }

        private static PerformanceCounter CreateCounter()
        {
            PerformanceCounter created = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
            try
            {
                created.NextValue();
                return created;
            }
            catch
            {
                created.Dispose();
                throw;
            }
        }
    }

    internal static class MaximGuard
    {
        private static string StateFile { get { return Path.Combine(Branding.DataDirectory, "maxim-observation.txt"); } }

        public static int Observe(MaximInfo info)
        {
            try
            {
                Directory.CreateDirectory(Branding.DataDirectory);
                int previousStreak = 0;
                DateTime previousTime = DateTime.MinValue;
                long lastAttemptTicks = 0;
                string[] fields;
                if (File.Exists(StateFile))
                {
                    fields = File.ReadAllText(StateFile, Encoding.UTF8).Trim().Split('|');
                    if (fields.Length >= 2)
                    {
                        long ticks;
                        int streak;
                        if (long.TryParse(fields[0], out ticks)) previousTime = new DateTime(ticks, DateTimeKind.Utc);
                        if (int.TryParse(fields[1], out streak)) previousStreak = streak;
                    }
                    if (fields.Length >= 6) long.TryParse(fields[5], out lastAttemptTicks);
                }
                bool highAndVerified = info.Handles >= 10000 && info.Verified && !string.IsNullOrEmpty(info.ServiceName);
                int next = 0;
                if (highAndVerified)
                    next = DateTime.UtcNow - previousTime <= TimeSpan.FromMinutes(10) ? Math.Min(99, previousStreak + 1) : 1;
                string content = DateTime.UtcNow.Ticks + "|" + next + "|" + info.Handles + "|" + (info.Verified ? "1" : "0") + "|" + info.ServiceName + "|" + lastAttemptTicks;
                File.WriteAllText(StateFile, content, Encoding.UTF8);
                return next;
            }
            catch { return 0; }
        }

        public static bool HasConfirmedTwoReadings(MaximInfo current)
        {
            try
            {
                TimeSpan remaining;
                if (IsInCooldown(out remaining)) return false;
                if (!current.Verified || current.Handles < 10000 || string.IsNullOrEmpty(current.ServiceName) || !File.Exists(StateFile)) return false;
                string[] fields = File.ReadAllText(StateFile, Encoding.UTF8).Trim().Split('|');
                if (fields.Length < 5) return false;
                long ticks;
                int streak;
                long previousHandles;
                if (!long.TryParse(fields[0], out ticks) || !int.TryParse(fields[1], out streak) || !long.TryParse(fields[2], out previousHandles)) return false;
                DateTime time = new DateTime(ticks, DateTimeKind.Utc);
                return DateTime.UtcNow - time <= TimeSpan.FromMinutes(2) &&
                    streak >= 2 &&
                    previousHandles >= 10000 &&
                    fields[3] == "1" &&
                    string.Equals(fields[4], current.ServiceName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool IsInCooldown(out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            try
            {
                if (!File.Exists(StateFile)) return false;
                string[] fields = File.ReadAllText(StateFile, Encoding.UTF8).Trim().Split('|');
                if (fields.Length < 6) return false;
                long ticks;
                if (!long.TryParse(fields[5], out ticks) || ticks <= 0) return false;
                DateTime attempted = new DateTime(ticks, DateTimeKind.Utc);
                TimeSpan elapsed = DateTime.UtcNow - attempted;
                if (elapsed >= TimeSpan.FromMinutes(30)) return false;
                remaining = TimeSpan.FromMinutes(30) - elapsed;
                return true;
            }
            catch { return false; }
        }

        public static void RecordRestartAttempt()
        {
            try
            {
                Directory.CreateDirectory(Branding.DataDirectory);
                File.WriteAllText(StateFile, DateTime.UtcNow.Ticks + "|0|0|0||" + DateTime.UtcNow.Ticks, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal sealed class CleanOperation
    {
        public string Text = "";
        public int Changed;
        public int Protected;
        public int Failed;
    }

    internal static class Cleaner
    {
        public static string RunSafe()
        {
            List<string> actions = new List<string>();
            CpuSampler cpu = new CpuSampler();
            DiskSampler disk = new DiskSampler();
            cpu.Sample();
            disk.Sample();
            Thread.Sleep(700);
            Snapshot before = Snapshot.Capture(cpu.Sample(), disk.Sample());

            actions.Add("[조치 전] CPU " + before.CpuPercent.ToString("0.0") + "% · RAM " + before.MemoryUsedPercent.ToString("0.0") + "% · 커밋 " + before.CommitPercent.ToString("0.0") + "% · 핸들 " + before.HandleCount.ToString("N0") + " · Maxim " + before.Maxim.Handles.ToString("N0"));
            bool resourcePressure = before.CpuPercent >= 70 || before.MemoryUsedPercent >= 80 || before.CommitPercent >= 75 || (before.DiskAvailable && before.DiskPercent >= 80);
            CleanOperation robocopy = LowerRobocopyPriority(resourcePressure);
            CleanOperation maxim = HandleMaxim(before.Maxim);
            actions.Add(robocopy.Text);
            actions.Add(maxim.Text);
            List<string> protectedNames = before.GetActiveProtectedNames();
            actions.Add("[보호 유지] " + (protectedNames.Count > 0 ? string.Join(" · ", protectedNames.ToArray()) : "현재 실행 중인 보호 대상 없음"));
            actions.Add("[파일] 사용자 파일과 임시파일을 삭제하지 않았습니다.");

            actions.Add("[측정] 조치 후 약 4초 기다려 다시 확인했습니다.");
            Thread.Sleep(3800);
            Snapshot after = Snapshot.Capture(cpu.Sample(), disk.Sample());
            actions.Add("[조치 후] CPU " + after.CpuPercent.ToString("0.0") + "% · RAM " + after.MemoryUsedPercent.ToString("0.0") + "% · 커밋 " + after.CommitPercent.ToString("0.0") + "% · 핸들 " + after.HandleCount.ToString("N0") + " · Maxim " + after.Maxim.Handles.ToString("N0"));
            actions.Add("[즉시 변화·자연 변동 포함] CPU " + Signed(after.CpuPercent - before.CpuPercent, "%") + " · RAM " + Signed(after.MemoryUsedPercent - before.MemoryUsedPercent, "%p") + " · 커밋 " + Signed(after.CommitPercent - before.CommitPercent, "%p") + " · 핸들 " + Signed(after.HandleCount - before.HandleCount));
            int changedCount = robocopy.Changed + maxim.Changed;
            int protectedCount = protectedNames.Count + robocopy.Protected + maxim.Protected;
            int failedCount = robocopy.Failed + maxim.Failed;
            actions.Add("[요약] 조치 " + changedCount + " / 보호 " + protectedCount + " / 실패 " + failedCount);
            if (failedCount > 0)
                actions.Add("[판정] 조치를 시도했지만 일부 사후 검증이 실패했습니다. 성공으로 단정하지 않으며 위 결과를 확인해 주세요.");
            else if (changedCount == 0)
                actions.Add("[판정] 현재 자동 조치할 이상 항목이 없어 변경하지 않았습니다. 수치가 거의 그대로인 것은 정상입니다.");
            else
                actions.Add("[판정] 확인된 항목 " + changedCount + "개를 안전하게 조치했습니다. 일반 앱을 종료하지 않아 RAM 변화는 작을 수 있습니다.");

            string result = string.Join(Environment.NewLine, actions.ToArray());
            AppLog.Write("안전 점검·조치 결과 | " + result.Replace("\r", " ").Replace("\n", " | "));
            disk.Dispose();
            return result;
        }

        private static string Signed(double value, string suffix)
        {
            return (value > 0 ? "+" : "") + value.ToString("0.0") + suffix;
        }

        private static string Signed(long value)
        {
            return (value > 0 ? "+" : "") + value.ToString("N0");
        }

        private static CleanOperation LowerRobocopyPriority(bool resourcePressure)
        {
            CleanOperation result = new CleanOperation();
            int changed = 0;
            int alreadyLow = 0;
            int found = 0;
            foreach (Process process in Process.GetProcessesByName("Robocopy"))
            {
                found++;
                try
                {
                    ProcessPriorityClass current = process.PriorityClass;
                    if (!resourcePressure)
                    {
                        result.Protected++;
                    }
                    else if (current == ProcessPriorityClass.BelowNormal || current == ProcessPriorityClass.Idle)
                        alreadyLow++;
                    else
                    {
                        process.PriorityClass = ProcessPriorityClass.BelowNormal;
                        changed++;
                    }
                }
                catch { result.Failed++; }
                finally { process.Dispose(); }
            }
            result.Changed = changed;
            if (found == 0) result.Text = "[Robocopy] 실행 중인 작업이 없습니다.";
            else if (!resourcePressure) result.Text = "[Robocopy] CPU·디스크·메모리 한도가 주의 기준 미만이라 " + found + "개 작업을 그대로 보호했습니다.";
            else if (changed > 0) result.Text = "[Robocopy] 종료하지 않고 " + changed + "개 작업의 우선순위만 낮췄습니다.";
            else if (alreadyLow > 0) result.Text = "[Robocopy] " + alreadyLow + "개 작업이 이미 낮은 우선순위입니다. 종료하지 않았습니다.";
            else result.Text = "[Robocopy] 우선순위를 변경하지 못했으며 작업은 종료하지 않았습니다.";
            return result;
        }

        private static CleanOperation HandleMaxim(MaximInfo info)
        {
            CleanOperation result = new CleanOperation();
            if (info.Handles < 10000)
            {
                result.Text = "[오디오] 자원 " + info.Handles.ToString("N0") + "개로 조치 기준 미만입니다.";
                return result;
            }
            if (!info.Verified || string.IsNullOrEmpty(info.ServiceName))
            {
                result.Protected = 1;
                result.Text = "[오디오] 자원 " + info.Handles.ToString("N0") + "개지만 대상을 확실히 검증하지 못해 경고만 기록했습니다. (" + info.Reason + ")";
                return result;
            }
            if (!string.Equals(info.ServiceName, "MaximAudioService", StringComparison.OrdinalIgnoreCase))
            {
                result.Protected = 1;
                result.Text = "[오디오] 정확한 MaximAudioService가 아니므로 변경하지 않았습니다.";
                return result;
            }
            TimeSpan cooldown;
            if (MaximGuard.IsInCooldown(out cooldown))
            {
                result.Protected = 1;
                result.Text = "[오디오] 최근 재시작 후 30분 보호 시간입니다. 약 " + Math.Ceiling(cooldown.TotalMinutes).ToString("0") + "분 뒤 다시 판단합니다.";
                return result;
            }
            if (!MaximGuard.HasConfirmedTwoReadings(info))
            {
                result.Protected = 1;
                result.Text = "[오디오] 자원 " + info.Handles.ToString("N0") + "개를 확인했지만 2회 연속 확인 조건이 충족되지 않아 조치하지 않았습니다.";
                return result;
            }

            bool restartAttempted = false;
            try
            {
                using (ServiceController controller = new ServiceController(info.ServiceName))
                {
                    controller.Refresh();
                    restartAttempted = true;
                    if (controller.Status != ServiceControllerStatus.Stopped && controller.Status != ServiceControllerStatus.StopPending)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(12));
                    }
                    controller.Refresh();
                    if (controller.Status == ServiceControllerStatus.Stopped)
                    {
                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(12));
                    }
                }
                MaximGuard.RecordRestartAttempt();

                int verifiedReadings = 0;
                long postHandles = long.MaxValue;
                bool serviceRunning = false;
                for (int i = 0; i < 3; i++)
                {
                    Thread.Sleep(1000);
                    Snapshot check = Snapshot.Capture(0, new DiskReading { Available = false, Percent = 0 });
                    if (check.Maxim.Verified && string.Equals(check.Maxim.ServiceName, "MaximAudioService", StringComparison.OrdinalIgnoreCase))
                    {
                        verifiedReadings++;
                        postHandles = check.Maxim.Handles;
                    }
                }
                try
                {
                    using (ServiceController checkController = new ServiceController("MaximAudioService"))
                    {
                        checkController.Refresh();
                        serviceRunning = checkController.Status == ServiceControllerStatus.Running;
                    }
                }
                catch { serviceRunning = false; }

                string recoveryNote = "";
                if (!serviceRunning && restartAttempted) serviceRunning = TryEnsureMaximRunning(out recoveryNote);

                if (serviceRunning && verifiedReadings >= 2 && postHandles < info.Handles && postHandles < 10000)
                {
                    result.Changed = 1;
                    result.Text = "[오디오] 검증된 MaximAudioService만 재시작했고, 사후 3회 중 " + verifiedReadings + "회 확인에서 자원이 " + postHandles.ToString("N0") + "개로 정상화됐습니다.";
                }
                else
                {
                    result.Changed = 1;
                    result.Failed = 1;
                    string observed = postHandles == long.MaxValue ? "확인 불가" : postHandles.ToString("N0");
                    result.Text = "[오디오] 서비스 재시작은 실행했지만 사후 검증 기준을 충족하지 못해 성공으로 단정하지 않습니다. (Running=" + serviceRunning + ", 확인 " + verifiedReadings + "/3, 자원 " + observed + (string.IsNullOrEmpty(recoveryNote) ? "" : ", " + recoveryNote) + ")";
                }
            }
            catch (Exception ex)
            {
                if (restartAttempted) MaximGuard.RecordRestartAttempt();
                string recoveryNote = "재시작 시도 전 오류";
                bool restored = !restartAttempted || TryEnsureMaximRunning(out recoveryNote);
                result.Failed = 1;
                result.Text = "[오디오] 검증된 서비스 재시작에 실패해 중단했습니다. (" + ex.Message + ") " + (restored ? "서비스 실행 상태를 확인했습니다." : "서비스 자동 복구도 실패했습니다: " + recoveryNote);
            }
            return result;
        }

        private static bool TryEnsureMaximRunning(out string note)
        {
            note = "";
            try
            {
                using (ServiceController controller = new ServiceController("MaximAudioService"))
                {
                    controller.Refresh();
                    if (controller.Status == ServiceControllerStatus.Running)
                    {
                        note = "서비스 실행 확인";
                        return true;
                    }
                    if (controller.Status == ServiceControllerStatus.StopPending)
                    {
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                        controller.Refresh();
                    }
                    if (controller.Status == ServiceControllerStatus.Stopped)
                    {
                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                    }
                    else if (controller.Status == ServiceControllerStatus.StartPending)
                        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                    controller.Refresh();
                    note = controller.Status == ServiceControllerStatus.Running ? "서비스 실행 복구" : "최종 상태 " + controller.Status;
                    return controller.Status == ServiceControllerStatus.Running;
                }
            }
            catch (Exception ex)
            {
                note = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }

    internal static class OfficeCareEngine
    {
        private const long OneDriveHandleThreshold = 250000;
        private const long ExplorerHandleThreshold = 20000;
        private const int ExplorerThreadThreshold = 300;
        public const bool HasFileDeletionCapability = false;

        public static bool IsTargetMachine()
        {
            return true;
        }

        public static bool IsTargetMachineName(string machineName)
        {
            return !string.IsNullOrWhiteSpace(machineName);
        }

        public static bool ShouldRestartOneDrive(long handles)
        {
            return handles >= OneDriveHandleThreshold;
        }

        public static bool ShouldRestartExplorer(long handles, int threads)
        {
            return handles >= ExplorerHandleThreshold || threads >= ExplorerThreadThreshold;
        }

        public static string BuildPlan()
        {
            StringBuilder report = new StringBuilder();
            long oneDriveHandles = GetTotalHandles("OneDrive");
            int oneDriveCount = GetProcessCount("OneDrive");
            int robocopyCount = GetProcessCount("Robocopy");
            int pythonCount = GetProcessCount("pythonw");
            int glanceCount = GetProcessCount("Glance");
            int crossDeviceCount = GetProcessCount("CrossDeviceService");
            int searchHostCount = GetProcessCount("SearchFilterHost") + GetProcessCount("SearchProtocolHost");
            bool everythingActive = GetProcessCount("Everything") > 0;
            long explorerHandles;
            int explorerThreads;
            GetExplorerLoad(out explorerHandles, out explorerThreads);
            bool sttActive = IsSttTranscriptionActive();
            bool sttIdleManaged = IsSttIdlePolicyCurrent();
            long dexHandles = GetTotalHandles("SamsungDeX");
            long imageSaferHandles = GetTotalHandles("IMGSF50Svc");

            report.AppendLine("컴퓨터: " + Environment.MachineName);
            report.AppendLine("적용 프로필: " + ProfileDetector.DisplayName + " (" + Branding.BuildProfile + ")");
            report.AppendLine();
            report.AppendLine("현재 확인");
            report.AppendLine("· OneDrive " + oneDriveCount + "개 / 핸들 " + oneDriveHandles.ToString("N0"));
            report.AppendLine("· Windows 탐색기 핸들 " + explorerHandles.ToString("N0") + " / 스레드 " + explorerThreads.ToString("N0"));
            report.AppendLine("· Robocopy " + robocopyCount + "개 / Python 백그라운드 " + pythonCount + "개");
            if (ProfileDetector.Current == DeviceProfile.Laptop)
                report.AppendLine("· 노트북 보조 작업: Glance " + glanceCount + "개 / 휴대폰 연결 " + crossDeviceCount + "개");
            if (ProfileDetector.Current == DeviceProfile.OfficeCorea || GetServiceStatusText("RicohDeviceSoftwareManager") != "미설치")
                report.AppendLine("· RICOH 관리 " + GetServiceStatusText("RicohDeviceSoftwareManager") + " / Windows 검색 " + GetServiceStatusText("WSearch") + " / 검색 호스트 " + searchHostCount + "개");
            if (sttActive) report.AppendLine("· 중요 작업: 법률 녹취 자동전사(STT) · " + (sttIdleManaged ? GetSttIdlePolicyDisplay() : "수동 보호 중"));

            report.AppendLine();
            report.AppendLine("실행 예정");
            report.AppendLine(ShouldRestartOneDrive(oneDriveHandles)
                ? "· OneDrive 폭주를 정상 종료 후 다시 시작 (동기화 자동 재개)"
                : "· OneDrive는 임계값 미만이므로 유지");
            report.AppendLine(ShouldRestartExplorer(explorerHandles, explorerThreads)
                ? "· 누적된 Windows 탐색기를 다시 시작 (작업표시줄은 자동 복구, 열린 폴더 창은 닫힐 수 있음)"
                : "· Windows 탐색기는 임계값 미만이므로 유지");
            if (ProfileDetector.Current == DeviceProfile.OfficeCorea || GetServiceStatusText("RicohDeviceSoftwareManager") != "미설치")
                report.AppendLine("· RICOH 자동관리만 일시정지 (인쇄 서비스는 유지)");
            report.AppendLine(everythingActive
                ? "· Everything이 있으므로 Windows 검색색인을 현재 세션에서 일시정지"
                : "· Everything이 없어 Windows 검색색인은 유지");
            report.AppendLine("· Robocopy는 종료하지 않고 우선순위만 낮춤");
            report.AppendLine(sttActive
                ? (sttIdleManaged
                    ? "· 법률 녹취 STT는 PC 사용 중 대기하고 10분 무입력 후 자동 재개"
                    : "· 법률 녹취 STT는 다음 확인창에서 동의할 때만 일시중지")
                : "· 확인되지 않은 Python 작업은 종료하지 않고 우선순위만 낮춤");
            if (ProfileDetector.Current == DeviceProfile.Laptop)
                report.AppendLine("· Glance·휴대폰 연결은 종료하지 않고 우선순위만 낮춤");

            report.AppendLine();
            report.AppendLine("보호 유지");
            report.AppendLine("· 사용자 파일 · Google Drive · Windows 보안 · CHKDSK · 브라우저 · Office");
            if (dexHandles >= 100000) report.AppendLine("· Samsung DeX 핸들 " + dexHandles.ToString("N0") + "개: 자동 종료하지 않고 경고만 기록");
            if (imageSaferHandles >= 100000) report.AppendLine("· Image SAFER 핸들 " + imageSaferHandles.ToString("N0") + "개: 보안 서비스라 자동 종료하지 않음");
            if (ProfileDetector.Current == DeviceProfile.Laptop)
                report.AppendLine("· 노트북 카메라·폰 연결·제조사 유틸리티는 자동 종료하지 않음");
            return report.ToString().Trim();
        }

        public static string RunUserStage()
        {
            List<string> actions = new List<string>();
            actions.Add("[사용자 영역] " + ProfileDetector.DisplayName + " 기준으로 확인했습니다.");
            actions.Add(RestartOneDriveIfRunaway());
            actions.Add(RestartExplorerIfRunaway());
            actions.Add(LowerPriority("Robocopy", "Robocopy"));
            actions.Add(LowerPriority("pythonw", "보호 중인 Python 백그라운드"));
            if (ProfileDetector.Current == DeviceProfile.Laptop)
            {
                actions.Add(LowerPriority("Glance", "Glance 카메라 감지"));
                actions.Add(LowerPriority("CrossDeviceService", "휴대폰 연결"));
            }

            long dexHandles = GetTotalHandles("SamsungDeX");
            long imageSaferHandles = GetTotalHandles("IMGSF50Svc");
            if (dexHandles >= 100000)
                actions.Add("[보호] Samsung DeX 핸들 " + dexHandles.ToString("N0") + "개를 확인했지만 자동 종료하지 않았습니다.");
            if (imageSaferHandles >= 100000)
                actions.Add("[보호] Image SAFER 핸들 " + imageSaferHandles.ToString("N0") + "개를 확인했지만 보안 서비스라 변경하지 않았습니다.");
            actions.Add("[파일] 사용자 파일·임시파일·다운로드·휴지통을 삭제하지 않았습니다.");
            return string.Join(Environment.NewLine, actions.ToArray());
        }

        public static string RunElevatedStage(bool pauseStt)
        {
            List<string> actions = new List<string>();
            actions.Add("[관리자 영역] " + ProfileDetector.DisplayName + " 서비스 상태를 확인했습니다.");
            if (ProfileDetector.Current == DeviceProfile.OfficeCorea || GetServiceStatusText("RicohDeviceSoftwareManager") != "미설치")
            {
                actions.Add(StopServiceAndProcesses(
                    "RicohDeviceSoftwareManager",
                    "RICOH 자동관리",
                    new string[] { "rorchsvc", "rorchpdr", "rorchcdk" }));
            }

            if (GetProcessCount("Everything") > 0)
            {
                actions.Add(StopServiceAndProcesses(
                    "WSearch",
                    "Windows 검색색인",
                    new string[] { "SearchFilterHost", "SearchProtocolHost" }));
            }
            else
            {
                actions.Add("[검색색인] Everything이 실행 중이 아니어서 Windows 검색을 보호했습니다.");
            }

            if (IsSttTranscriptionActive())
                actions.Add(IsSttIdlePolicyCurrent()
                    ? "[전사 자동관리] " + GetSttIdlePolicyDisplay() + " — PC 사용 중 대기 / 10분 무입력 후 자동 재개 정책을 유지했습니다."
                    : (pauseStt ? PauseSttTranscription() : "[중요 작업 보호] 법률 녹취 자동전사(STT)를 계속 실행했습니다."));

            actions.Add("[보호] Google Drive·Windows 보안·금융 보안·브라우저·Office는 변경하지 않았습니다.");
            return string.Join(Environment.NewLine, actions.ToArray());
        }

        public static string CaptureSummary(string label)
        {
            CpuSampler cpu = new CpuSampler();
            DiskSampler disk = new DiskSampler();
            ProcessCpuSampler processCpu = new ProcessCpuSampler();
            try
            {
                cpu.Sample();
                disk.Sample();
                processCpu.Sample();
                Thread.Sleep(1600);
                Snapshot snapshot = Snapshot.Capture(cpu.Sample(), disk.Sample());
                CpuHotspot hotspot = processCpu.Sample();
                string result = "[" + label + "] CPU " + snapshot.CpuPercent.ToString("0.0") + "% · RAM " +
                    snapshot.MemoryUsedPercent.ToString("0.0") + "% · 커밋 " + snapshot.CommitPercent.ToString("0.0") +
                    "% · 시스템 핸들 " + snapshot.HandleCount.ToString("N0") + " · OneDrive 핸들 " +
                    GetTotalHandles("OneDrive").ToString("N0");
                if (hotspot.Available) result += Environment.NewLine + "[실시간 CPU 상위] " + hotspot.Summary;
                return result;
            }
            finally
            {
                disk.Dispose();
            }
        }

        private static string RestartOneDriveIfRunaway()
        {
            Process[] processes = Process.GetProcessesByName("OneDrive");
            long beforeHandles = SumHandles(processes);
            string executable = FindOneDriveExecutable(processes);
            DisposeAll(processes);

            if (!ShouldRestartOneDrive(beforeHandles))
                return "[OneDrive] 핸들 " + beforeHandles.ToString("N0") + "개로 임계값 미만이라 유지했습니다.";
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                return "[OneDrive] 핸들 폭주를 확인했지만 실행 파일을 검증하지 못해 재시작하지 않았습니다.";

            try
            {
                ProcessStartInfo shutdownInfo = new ProcessStartInfo(executable, "/shutdown");
                shutdownInfo.UseShellExecute = false;
                shutdownInfo.CreateNoWindow = true;
                shutdownInfo.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process shutdown = Process.Start(shutdownInfo))
                {
                    if (shutdown != null) shutdown.WaitForExit(12000);
                }

                WaitForProcessExit("OneDrive", 8000);
                Process[] remaining = Process.GetProcessesByName("OneDrive");
                foreach (Process process in remaining)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }

                Thread.Sleep(1200);
                ProcessStartInfo startInfo = new ProcessStartInfo(executable, "/background");
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Path.GetDirectoryName(executable);
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                Process started = Process.Start(startInfo);
                if (started != null) started.Dispose();

                bool running = WaitForProcessStart("OneDrive", 15000);
                long afterHandles = GetTotalHandles("OneDrive");
                if (running && afterHandles < beforeHandles)
                    return "[OneDrive] 핸들 " + beforeHandles.ToString("N0") + " → " + afterHandles.ToString("N0") + "개로 정상 재시작했습니다. 동기화는 자동 재개됩니다.";
                return "[OneDrive] 재시작을 실행했지만 사후 확인이 충분하지 않습니다. 현재 핸들 " + afterHandles.ToString("N0") + "개입니다.";
            }
            catch (Exception ex)
            {
                return "[OneDrive] 안전 재시작 실패: " + ex.GetType().Name + " · " + ex.Message;
            }
        }

        private static void GetExplorerLoad(out long maxHandles, out int maxThreads)
        {
            maxHandles = 0;
            maxThreads = 0;
            foreach (Process process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    maxHandles = Math.Max(maxHandles, process.HandleCount);
                    maxThreads = Math.Max(maxThreads, process.Threads.Count);
                }
                catch { }
                finally { process.Dispose(); }
            }
        }

        private static string RestartExplorerIfRunaway()
        {
            Process selected = null;
            long beforeHandles = 0;
            int beforeThreads = 0;
            foreach (Process process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    long handles = process.HandleCount;
                    int threads = process.Threads.Count;
                    if (selected == null || handles > beforeHandles)
                    {
                        if (selected != null) selected.Dispose();
                        selected = process;
                        beforeHandles = handles;
                        beforeThreads = threads;
                    }
                    else process.Dispose();
                }
                catch { process.Dispose(); }
            }

            if (selected == null) return "[Windows 탐색기] 실행 중인 기본 탐색기를 찾지 못했습니다.";
            if (!ShouldRestartExplorer(beforeHandles, beforeThreads))
            {
                selected.Dispose();
                return "[Windows 탐색기] 핸들 " + beforeHandles.ToString("N0") + " / 스레드 " + beforeThreads.ToString("N0") + "로 임계값 미만이라 유지했습니다.";
            }

            try
            {
                selected.Kill();
                selected.WaitForExit(10000);
                selected.Dispose();
                Thread.Sleep(900);
                string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                ProcessStartInfo start = new ProcessStartInfo(explorerPath);
                start.UseShellExecute = true;
                Process restarted = Process.Start(start);
                if (restarted != null) restarted.Dispose();
                Thread.Sleep(2200);
                long afterHandles;
                int afterThreads;
                GetExplorerLoad(out afterHandles, out afterThreads);
                return "[Windows 탐색기] 핸들 " + beforeHandles.ToString("N0") + " → " + afterHandles.ToString("N0") +
                    " / 스레드 " + beforeThreads.ToString("N0") + " → " + afterThreads.ToString("N0") + "로 재시작했습니다.";
            }
            catch (Exception ex)
            {
                if (selected != null) selected.Dispose();
                return "[Windows 탐색기] 안전 재시작 실패: " + ex.GetType().Name + " · " + ex.Message;
            }
        }

        public static bool IsSttTranscriptionActive()
        {
            if (IsSttIdlePolicyCurrent()) return true;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='pythonw.exe'"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        string command = Convert.ToString(item["CommandLine"]);
                        if (!string.IsNullOrEmpty(command) && command.IndexOf("stt_run2.py", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static string SttIdleStatusPath
        {
            get { return @"C:\DriveBackupLog\stt_idle_status.json"; }
        }

        public static bool IsSttIdlePolicyCurrent()
        {
            try
            {
                if (!File.Exists(SttIdleStatusPath)) return false;
                string text = File.ReadAllText(SttIdleStatusPath, Encoding.UTF8);
                if (text.IndexOf("keyboard_mouse_idle_gate", StringComparison.OrdinalIgnoreCase) < 0) return false;
                Match match = Regex.Match(text, "\\\"updated\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                DateTime updated;
                return match.Success && DateTime.TryParse(match.Groups[1].Value, out updated) &&
                    DateTime.Now - updated < TimeSpan.FromSeconds(20);
            }
            catch { return false; }
        }

        public static string GetSttIdlePolicyDisplay()
        {
            try
            {
                if (!File.Exists(SttIdleStatusPath)) return "상태 확인 중";
                string text = File.ReadAllText(SttIdleStatusPath, Encoding.UTF8);
                Match stateMatch = Regex.Match(text, "\\\"state\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                Match idleMatch = Regex.Match(text, "\\\"idle_seconds\\\"\\s*:\\s*([0-9.]+)");
                string state = stateMatch.Success ? stateMatch.Groups[1].Value : "";
                double idle = 0;
                if (idleMatch.Success) double.TryParse(idleMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out idle);
                if (state == "waiting_for_idle") return "PC 사용 중 대기 · 무입력 " + Math.Floor(idle / 60).ToString("0") + "분";
                if (state == "running") return "10분 유휴 확인 · 전사 실행 중";
                if (state == "completed") return "전사 대기열 완료";
            }
            catch { }
            return "전사 자동관리 상태 확인 중";
        }

        private static string PauseSttTranscription()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("schtasks.exe", "/End /TN \"\\STT_Transcribe_2\"");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(info))
                {
                    if (process == null) return "[법률 녹취 STT] 예약 작업 종료 명령을 시작하지 못했습니다.";
                    if (!process.WaitForExit(15000)) return "[법률 녹취 STT] 일시중지 확인 시간이 초과됐습니다. 강제 종료하지 않았습니다.";
                    if (process.ExitCode != 0) return "[법률 녹취 STT] 예약 작업을 일시중지하지 못했습니다. 오류 코드 " + process.ExitCode + ".";
                }
                for (int i = 0; i < 20 && IsSttTranscriptionActive(); i++) Thread.Sleep(250);
                return IsSttTranscriptionActive()
                    ? "[법률 녹취 STT] 종료 명령 후에도 작업이 남아 있어 강제 종료하지 않았습니다."
                    : "[법률 녹취 STT] 사용자의 동의에 따라 일시중지했습니다. 원본과 완료본은 보존되며 다음 로그인 때 이어받습니다.";
            }
            catch (Exception ex)
            {
                return "[법률 녹취 STT] 일시중지 실패: " + ex.GetType().Name + " · " + ex.Message;
            }
        }

        private static string LowerPriority(string processName, string label)
        {
            int found = 0;
            int changed = 0;
            int alreadyLow = 0;
            int failed = 0;
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                found++;
                try
                {
                    ProcessPriorityClass current = process.PriorityClass;
                    if (current == ProcessPriorityClass.BelowNormal || current == ProcessPriorityClass.Idle)
                        alreadyLow++;
                    else
                    {
                        process.PriorityClass = ProcessPriorityClass.BelowNormal;
                        changed++;
                    }
                }
                catch { failed++; }
                finally { process.Dispose(); }
            }

            if (found == 0) return "[우선순위] " + label + " 작업이 없습니다.";
            return "[우선순위] " + label + " " + found + "개 중 변경 " + changed + " / 이미 낮음 " + alreadyLow + " / 실패 " + failed + " — 종료하지 않았습니다.";
        }

        private static string StopServiceAndProcesses(string serviceName, string label, string[] processNames)
        {
            int changed = 0;
            int failed = 0;
            string serviceState = "미설치";
            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    controller.Refresh();
                    serviceState = controller.Status.ToString();
                    if (controller.Status != ServiceControllerStatus.Stopped && controller.Status != ServiceControllerStatus.StopPending)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(18));
                        changed++;
                    }
                    controller.Refresh();
                    serviceState = controller.Status.ToString();
                }
            }
            catch (InvalidOperationException)
            {
                serviceState = "미설치";
            }
            catch (Exception ex)
            {
                failed++;
                serviceState = "확인 실패 " + ex.GetType().Name;
            }

            foreach (string processName in processNames)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                        changed++;
                    }
                    catch { failed++; }
                    finally { process.Dispose(); }
                }
            }

            return "[" + label + "] 서비스 " + serviceState + " / 조치 " + changed + " / 실패 " + failed + " — 시작 유형은 바꾸지 않았으며 Windows가 자동으로 다시 시작할 수 있습니다.";
        }

        private static string GetServiceStatusText(string serviceName)
        {
            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    controller.Refresh();
                    return controller.Status == ServiceControllerStatus.Running ? "실행 중" : controller.Status.ToString();
                }
            }
            catch { return "미설치"; }
        }

        private static int GetProcessCount(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            int count = processes.Length;
            DisposeAll(processes);
            return count;
        }

        private static long GetTotalHandles(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            long total = SumHandles(processes);
            DisposeAll(processes);
            return total;
        }

        private static long SumHandles(Process[] processes)
        {
            long total = 0;
            foreach (Process process in processes)
            {
                try { total += process.HandleCount; }
                catch { }
            }
            return total;
        }

        private static void DisposeAll(Process[] processes)
        {
            foreach (Process process in processes)
            {
                try { process.Dispose(); }
                catch { }
            }
        }

        private static string FindOneDriveExecutable(Process[] processes)
        {
            foreach (Process process in processes)
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
                catch { }
            }

            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "OneDrive.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft OneDrive", "OneDrive.exe")
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return "";
        }

        private static bool WaitForProcessExit(string processName, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (GetProcessCount(processName) == 0) return true;
                Thread.Sleep(250);
            }
            return GetProcessCount(processName) == 0;
        }

        private static bool WaitForProcessStart(string processName, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (GetProcessCount(processName) > 0) return true;
                Thread.Sleep(300);
            }
            return GetProcessCount(processName) > 0;
        }
    }

    internal static class StartupManager
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                return key != null && key.GetValue(Branding.RegistryValue) != null;
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (enabled) key.SetValue(Branding.RegistryValue, "\"" + Application.ExecutablePath + "\" --tray");
                else key.DeleteValue(Branding.RegistryValue, false);
            }
        }
    }

    internal static class AppLog
    {
        public static string LogPath { get { return Path.Combine(Branding.DataDirectory, "실행기록.txt"); } }

        public static void Write(string line)
        {
            try
            {
                Directory.CreateDirectory(Branding.DataDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2097152)
                {
                    string archived = Path.Combine(Branding.DataDirectory, "실행기록_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    File.Move(LogPath, archived);
                }
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + line + Environment.NewLine, new UTF8Encoding(true));
            }
            catch { }
        }
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FILETIME
        {
            public uint Low;
            public uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PERFORMANCE_INFORMATION
        {
            public int cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION performanceInformation, int size);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr processHandle, int flags, StringBuilder fileName, ref int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
            public uint ProcessId;
            public uint ServiceFlags;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr serviceControlManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatusEx(IntPtr service, int informationLevel, IntPtr buffer, int bufferSize, out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        internal static ulong ToUInt64(FILETIME value)
        {
            return ((ulong)value.High << 32) | value.Low;
        }

        internal static string TryGetProcessPath(Process process)
        {
            try
            {
                StringBuilder path = new StringBuilder(2048);
                int size = path.Capacity;
                if (QueryFullProcessImageName(process.Handle, 0, path, ref size)) return path.ToString();
            }
            catch { }
            return "";
        }

        internal static int TryGetServiceProcessId(string serviceName)
        {
            const uint ScManagerConnect = 0x0001;
            const uint ServiceQueryStatus = 0x0004;
            const int ServiceStatusProcessInfo = 0;
            IntPtr manager = IntPtr.Zero;
            IntPtr service = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                manager = OpenSCManager(null, null, ScManagerConnect);
                if (manager == IntPtr.Zero) return 0;
                service = OpenService(manager, serviceName, ServiceQueryStatus);
                if (service == IntPtr.Zero) return 0;

                int size = Marshal.SizeOf(typeof(SERVICE_STATUS_PROCESS));
                buffer = Marshal.AllocHGlobal(size);
                int needed;
                if (!QueryServiceStatusEx(service, ServiceStatusProcessInfo, buffer, size, out needed)) return 0;
                SERVICE_STATUS_PROCESS status = (SERVICE_STATUS_PROCESS)Marshal.PtrToStructure(buffer, typeof(SERVICE_STATUS_PROCESS));
                return unchecked((int)status.ProcessId);
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (service != IntPtr.Zero) CloseServiceHandle(service);
                if (manager != IntPtr.Zero) CloseServiceHandle(manager);
            }
        }
    }
}
