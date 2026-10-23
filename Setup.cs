// ScreenPen 설치 / 제거 프로그램 (C# 5, WinForms)
//   ScreenPen_Setup.exe          : 설치 창 표시
//   ScreenPen_Setup.exe /S       : 조용히 설치 (새 설치는 옵션 없이, 업데이트는 기존 바로가기 설정 유지)
//   Uninstall.exe /uninstall     : 제거 (설정 > 앱 > 설치된 앱 에서 호출됨)
//   Uninstall.exe /uninstall /S  : 확인 없이 조용히 제거
// 사용자 단위(현재 사용자) 설치라 관리자 권한이 필요 없습니다.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ScreenPen Setup")]
[assembly: AssemblyProduct("ScreenPen")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace ScreenPenSetup
{
    static class Installer
    {
        public const string AppName = "ScreenPen";
        public const string Version = "1.1.0";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenPen";

        public static string InstallDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ScreenPen");
            }
        }
        public static string AppExe { get { return Path.Combine(InstallDir, "ScreenPen.exe"); } }
        public static string UninstallExe { get { return Path.Combine(InstallDir, "Uninstall.exe"); } }
        public static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ScreenPen.lnk"); } }
        public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ScreenPen.lnk"); } }
        public static string StartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ScreenPen.lnk"); } }

        public static bool IsInstalled { get { return File.Exists(AppExe); } }
        // 업데이트 시 기존 선택을 유지하기 위해 현재 바로가기 상태를 읽는다
        public static bool HasDesktopShortcut { get { return File.Exists(DesktopLink); } }
        public static bool HasAutoStart { get { return File.Exists(StartupLink); } }

        static void KillApp()
        {
            foreach (Process p in Process.GetProcessesByName("ScreenPen"))
            {
                try { p.Kill(); p.WaitForExit(4000); } catch { }
            }
        }

        static void SetProp(object target, string name, object value)
        {
            target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new object[] { value });
        }

        static void MakeShortcut(string lnk, string target, string args)
        {
            Type sht = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(sht);
            object s = sht.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            SetProp(s, "TargetPath", target);
            SetProp(s, "Arguments", args ?? "");
            SetProp(s, "WorkingDirectory", Path.GetDirectoryName(target));
            SetProp(s, "IconLocation", target + ",0");
            SetProp(s, "Description", "화면에 바로 그리는 펜 (ScreenPen)");
            s.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
        }

        static void DeleteQuiet(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static void Install(bool desktopShortcut, bool autoStart)
        {
            KillApp();
            Directory.CreateDirectory(InstallDir);

            using (Stream rs = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenPen.exe"))
            {
                if (rs == null) throw new InvalidOperationException("설치 파일에 ScreenPen.exe 가 들어 있지 않습니다.");
                using (FileStream fs = File.Create(AppExe)) rs.CopyTo(fs);
            }

            string self = Application.ExecutablePath;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, UninstallExe, true);

            MakeShortcut(StartMenuLink, AppExe, null);
            if (desktopShortcut) MakeShortcut(DesktopLink, AppExe, null); else DeleteQuiet(DesktopLink);
            if (autoStart) MakeShortcut(StartupLink, AppExe, "--startup"); else DeleteQuiet(StartupLink);

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppName);
                k.SetValue("DisplayIcon", AppExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + UninstallExe + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + UninstallExe + "\" /uninstall /S");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)((new FileInfo(AppExe).Length + new FileInfo(UninstallExe).Length) / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void Uninstall()
        {
            KillApp();
            DeleteQuiet(StartMenuLink);
            DeleteQuiet(DesktopLink);
            DeleteQuiet(StartupLink);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            // 실행 중인 Uninstall.exe 자신이 들어 있는 폴더는 잠시 뒤 별도 프로세스가 삭제
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }
    }

    class SetupForm : Form
    {
        readonly Label title, desc, note;
        readonly CheckBox chkDesktop, chkAuto, chkRun;
        readonly Button btnMain, btnCancel;
        bool done;

        readonly float scale;
        int S(int v) { return (int)Math.Round(v * scale); }

        public SetupForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;

            Text = "ScreenPen 설치";
            Font = new Font("Malgun Gothic", 9.5f);
            AutoScaleMode = AutoScaleMode.None; // 좌표는 아래에서 화면 배율(scale)로 직접 계산
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(470), S(300));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            title = new Label { Left = S(24), Top = S(20), Width = S(420), Height = S(40), Font = new Font("Malgun Gothic", 16f, FontStyle.Bold) };
            title.Text = Installer.IsInstalled ? "ScreenPen 업데이트" : "ScreenPen 설치";
            desc = new Label { Left = S(26), Top = S(66), Width = S(420), Height = S(62) };
            desc.Text = "화면 위에 바로 그리는 펜 프로그램입니다.\r\n설치 위치: " + Installer.InstallDir + "\r\n(관리자 권한이 필요 없습니다)";
            chkDesktop = new CheckBox { Left = S(28), Top = S(136), Width = S(420), Height = S(28), Text = "바탕화면에 바로가기 만들기", Checked = Installer.HasDesktopShortcut };
            chkAuto = new CheckBox { Left = S(28), Top = S(166), Width = S(420), Height = S(28), Text = "Windows 로그인 시 자동 실행 (마우스 통과 모드로 시작)", Checked = Installer.HasAutoStart };
            chkRun = new CheckBox { Left = S(28), Top = S(136), Width = S(420), Height = S(28), Text = "지금 ScreenPen 실행", Checked = true, Visible = false };
            note = new Label { Left = S(28), Top = S(204), Width = S(420), Height = S(44), ForeColor = Color.Gray };
            note.Text = "실행 중인 ScreenPen 이 있으면 자동으로 종료한 뒤 설치합니다.\r\n제거: 설정 > 앱 > 설치된 앱 > ScreenPen";
            btnMain = new Button { Left = S(262), Top = S(250), Width = S(96), Height = S(36), Text = Installer.IsInstalled ? "업데이트" : "설치" };
            btnCancel = new Button { Left = S(366), Top = S(250), Width = S(86), Height = S(36), Text = "취소" };
            btnMain.Click += OnMain;
            btnCancel.Click += delegate { Close(); };
            AcceptButton = btnMain;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { title, desc, chkDesktop, chkAuto, chkRun, note, btnMain, btnCancel });
        }
        void OnMain(object sender, EventArgs e)
        {
            if (done)
            {
                if (chkRun.Checked)
                {
                    try { Process.Start(new ProcessStartInfo(Installer.AppExe) { WorkingDirectory = Installer.InstallDir }); } catch { }
                }
                Close();
                return;
            }

            btnMain.Enabled = false;
            btnCancel.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                Installer.Install(chkDesktop.Checked, chkAuto.Checked);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show("설치 중 오류가 발생했습니다.\r\n\r\n" + ex.Message, "ScreenPen 설치", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnMain.Enabled = true;
                btnCancel.Enabled = true;
                return;
            }
            Cursor = Cursors.Default;

            done = true;
            title.Text = "설치가 완료되었습니다";
            desc.Text = "시작 메뉴에서 \"ScreenPen\" 을 검색하거나 앱 목록에서 실행할 수 있습니다.\r\n설정 > 앱 > 설치된 앱 에서 제거할 수 있습니다.";
            chkDesktop.Visible = false;
            chkAuto.Visible = false;
            note.Visible = false;
            chkRun.Visible = true;
            btnMain.Text = "마침";
            btnMain.Enabled = true;
            btnCancel.Visible = false;
            AcceptButton = btnMain;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool silent = Array.IndexOf(args, "/S") >= 0 || Array.IndexOf(args, "/s") >= 0;
            bool uninstall = Array.IndexOf(args, "/uninstall") >= 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (uninstall)
                {
                    if (!silent && MessageBox.Show("ScreenPen 을(를) 제거할까요?", "ScreenPen 제거",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
                    Installer.Uninstall();
                    if (!silent) MessageBox.Show("ScreenPen 이(가) 제거되었습니다.", "ScreenPen 제거", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (silent)
                {
                    Installer.Install(Installer.HasDesktopShortcut, Installer.HasAutoStart); // 업데이트면 기존 선택 유지
                    return 0;
                }
                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show(ex.Message, "ScreenPen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
    }
}
