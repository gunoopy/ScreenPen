// ScreenPen - 화면 위에 바로 그리는 펜 프로그램 (Epic Pen 스타일)
// C# 5 / WPF (.NET Framework 4.x) — 추가 설치 없이 build.ps1 로 빌드됩니다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ScreenPen
{
    enum Tool { Pen, Highlighter, Eraser, Line, Arrow, Rect, Ellipse }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // WPF 가 초기화되기 전에 설정해야 한다.
            // 1) 모니터별 DPI 대응 (app.manifest 의 PerMonitorV2 와 함께 동작)
            // 2) WM_POINTER 기반 터치/펜 입력. 단, WPF 가 계산한 터치 좌표는 해상도·배율이 다른 보조 모니터에서
            //    어긋나므로, 오버레이 창은 WM_POINTER 를 WPF 보다 먼저 가로채 화면 좌표를 직접 환산한다 (OverlayWindow.PointerHook).
            //    (DisableStylusAndTouchSupport 로 WPF 터치를 아예 끄면 InkCanvas 가 시작 중에 죽는 WPF 버그가 있다)
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            // csc 로 직접 빌드하면 대상 프레임워크 정보가 없어 WPF 가 4.0 호환 모드로 동작하므로, 최신 다중 DPI 처리를 직접 켠다
            AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false);
            AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier3OrGreater", false);
            AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);

            bool created;
            using (Mutex mutex = new Mutex(true, "ScreenPen.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("ScreenPen 이 이미 실행 중입니다.\n화면 오른쪽 아래 트레이 아이콘을 확인하세요.",
                        "ScreenPen", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown; // 창이 여러 개라 Board.Quit 에서 직접 종료
                bool startup = Array.IndexOf(args, "--startup") >= 0; // 시작프로그램 실행: 마우스 통과 모드로 시작
                Board board = new Board(startup);
                board.Start();
                app.Run();
            }
        }
    }

    static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        public const uint MONITORINFOF_PRIMARY = 1;

        // 터치 / 펜 (WM_POINTER)
        public const int WM_POINTERUPDATE = 0x0245, WM_POINTERDOWN = 0x0246, WM_POINTERUP = 0x0247, WM_POINTERCAPTURECHANGED = 0x024C;
        public const uint PT_TOUCH = 2, PT_PEN = 3;
        public const uint POINTER_FLAG_INCONTACT = 0x4, POINTER_FLAG_CANCELED = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);

        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMonitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] public static extern bool GetPointerType(uint pointerId, out uint type);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rc);
        [DllImport("user32.dll")] public static extern bool SetWindowFeedbackSetting(IntPtr hWnd, int feedback, uint flags, uint size, ref int config);
    }

    // ───────────────────────── 모니터 정보 (물리 픽셀 단위) ─────────────────────────
    class ScreenInfo
    {
        public string Name;
        public Native.RECT Bounds;
        public Native.RECT Work;
        public bool Primary;
        public double Scale; // 1.0 = 100%

        public static List<ScreenInfo> All()
        {
            List<ScreenInfo> list = new List<ScreenInfo>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr hdc, ref Native.RECT rc, IntPtr data)
            {
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(h, ref mi)) return true;
                uint dx, dy;
                double scale = Native.GetDpiForMonitor(h, 0, out dx, out dy) == 0 ? dx / 96.0 : 1.0;
                ScreenInfo s = new ScreenInfo();
                s.Name = mi.szDevice;
                s.Bounds = mi.rcMonitor;
                s.Work = mi.rcWork;
                s.Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                s.Scale = scale;
                list.Add(s);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }

    // ───────────────────────── 전체 상태 (도구 / 색 / 실행 취소 / 단축키 / 트레이) ─────────────────────────
    // 모니터마다 오버레이 창을 하나씩 둔다. 창 하나가 여러 모니터에 걸치면 배율이 다른 모니터에서
    // 펜 좌표가 어긋나기 때문. 도구·색·실행 취소 기록은 모든 창이 공유한다.
    class Board
    {
        class Change
        {
            public StrokeCollection Target;
            public StrokeCollection Added;
            public StrokeCollection Removed;
        }
        class UndoAction
        {
            public readonly List<Change> Changes = new List<Change>();
        }

        public static readonly Color[] Palette = new Color[]
        {
            Color.FromRgb(255, 59, 48),   // 빨강
            Color.FromRgb(255, 149, 0),   // 주황
            Color.FromRgb(255, 214, 10),  // 노랑
            Color.FromRgb(52, 199, 89),   // 초록
            Color.FromRgb(10, 132, 255),  // 파랑
            Color.FromRgb(191, 90, 242),  // 보라
            Color.FromRgb(255, 255, 255), // 흰색
            Color.FromRgb(0, 0, 0)        // 검정
        };

        public Tool CurrentTool = Tool.Pen;
        public Color CurrentColor = Palette[0];
        public static readonly double[] PenSizes = new double[] { 3, 5, 12 };
        public static readonly double[] HighlighterSizes = new double[] { 14, 24, 40 };
        public int SizeLevel = 1; // 0 얇게, 1 보통, 2 굵게
        public double PenSize { get { return PenSizes[SizeLevel]; } }
        public double HighlighterSize { get { return HighlighterSizes[SizeLevel]; } }
        public bool DrawMode = true;
        public event Action ModelChanged;

        public readonly List<OverlayWindow> Overlays = new List<OverlayWindow>();
        ToolbarWindow toolbar;
        System.Windows.Forms.NotifyIcon tray;
        HwndSource hotkeySink;
        bool quitting;

        readonly Stack<UndoAction> undoStack = new Stack<UndoAction>();
        readonly Stack<UndoAction> redoStack = new Stack<UndoAction>();
        bool suppressHistory;

        const int HK_DRAW = 1, HK_CLEAR = 2, HK_TOOLBAR = 3, HK_QUIT = 4;

        readonly bool startPassThrough;

        public Board(bool startPassThrough)
        {
            this.startPassThrough = startPassThrough;
        }

        public bool CanUndo { get { return undoStack.Count > 0; } }
        public bool CanRedo { get { return redoStack.Count > 0; } }
        public bool IsShapeTool
        {
            get { return CurrentTool == Tool.Line || CurrentTool == Tool.Arrow || CurrentTool == Tool.Rect || CurrentTool == Tool.Ellipse; }
        }

        public OverlayWindow Primary
        {
            get
            {
                foreach (OverlayWindow o in Overlays) if (o.Screen.Primary) return o;
                return Overlays.Count > 0 ? Overlays[0] : null;
            }
        }

        public void Start()
        {
            // 전역 단축키는 보이지 않는 메시지 전용 창에서 받는다
            HwndSourceParameters p = new HwndSourceParameters("ScreenPenHotkeys");
            p.Width = 0; p.Height = 0; p.WindowStyle = 0;
            p.ParentWindow = Native.HWND_MESSAGE;
            hotkeySink = new HwndSource(p);
            hotkeySink.AddHook(WndProc);

            uint m = Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT;
            IntPtr h = hotkeySink.Handle;
            bool ok = Native.RegisterHotKey(h, HK_DRAW, m, (uint)KeyInterop.VirtualKeyFromKey(Key.D));
            ok &= Native.RegisterHotKey(h, HK_CLEAR, m, (uint)KeyInterop.VirtualKeyFromKey(Key.C));
            ok &= Native.RegisterHotKey(h, HK_TOOLBAR, m, (uint)KeyInterop.VirtualKeyFromKey(Key.T));
            ok &= Native.RegisterHotKey(h, HK_QUIT, m, (uint)KeyInterop.VirtualKeyFromKey(Key.Q));

            CreateTray();
            DrawMode = !startPassThrough;
            SyncScreens();

            toolbar = new ToolbarWindow(this);
            toolbar.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!quitting) { e.Cancel = true; toolbar.Hide(); } // Alt+F4 는 숨기기만
            };
            toolbar.Show();

            SetDrawMode(DrawMode);
            if (!ok) Toast("일부 전역 단축키가 다른 프로그램과 겹쳐 등록되지 못했습니다.");

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        }

        // 모니터 연결/해제·해상도·배율 변경 시 오버레이를 모니터 구성에 맞춘다
        void OnDisplayChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                if (quitting) return;
                SyncScreens();
                EnsureToolbarOnScreen();
            }));
        }

        void SyncScreens()
        {
            List<ScreenInfo> screens = ScreenInfo.All();
            foreach (OverlayWindow o in Overlays.ToArray())
            {
                ScreenInfo s = screens.Find(x => x.Name == o.Screen.Name);
                if (s == null) { Overlays.Remove(o); o.Detach(); }
                else o.SetScreen(s);
            }
            foreach (ScreenInfo s in screens)
            {
                if (Overlays.Exists(x => x.Screen.Name == s.Name)) continue;
                OverlayWindow ov = new OverlayWindow(this, s);
                Overlays.Add(ov);
                ApplyTool(ov);
                ov.Show();
            }
            RaiseToolbar();
        }

        public void Quit()
        {
            if (quitting) return;
            quitting = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            if (hotkeySink != null)
            {
                for (int i = HK_DRAW; i <= HK_QUIT; i++) Native.UnregisterHotKey(hotkeySink.Handle, i);
                hotkeySink.Dispose();
            }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            foreach (OverlayWindow o in Overlays.ToArray()) o.Detach();
            if (toolbar != null) toolbar.Close();
            Application.Current.Shutdown();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                handled = true;
                switch (wParam.ToInt32())
                {
                    case HK_DRAW: SetDrawMode(!DrawMode); break;
                    case HK_CLEAR: ClearAll(); break;
                    case HK_TOOLBAR: ToggleToolbar(); break;
                    case HK_QUIT: Quit(); break;
                }
            }
            return IntPtr.Zero;
        }

        // ───────────── 트레이 아이콘 ─────────────
        void CreateTray()
        {
            tray = new System.Windows.Forms.NotifyIcon();
            try
            {
                tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);
            }
            catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            tray.Text = "ScreenPen";
            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("그리기 / 마우스 통과 전환  (Ctrl+Shift+D)", null, delegate { SetDrawMode(!DrawMode); });
            menu.Items.Add("도구 모음 표시 / 숨기기  (Ctrl+Shift+T)", null, delegate { ToggleToolbar(); });
            menu.Items.Add("모두 지우기  (Ctrl+Shift+C)", null, delegate { ClearAll(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("종료  (Ctrl+Shift+Q)", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
            tray.MouseDoubleClick += delegate { ToggleToolbar(); };
            tray.Visible = true;
        }

        // ───────────── 도구 모음 위치 ─────────────
        IntPtr ToolbarHandle { get { return toolbar == null ? IntPtr.Zero : new WindowInteropHelper(toolbar).Handle; } }

        // 오버레이가 활성화되면 도구 모음보다 위로 올라오므로 다시 맨 위로 올린다
        public void RaiseToolbar()
        {
            IntPtr h = ToolbarHandle;
            if (h == IntPtr.Zero || !toolbar.IsVisible) return;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // 주 모니터 작업 영역 왼쪽, 세로 가운데 (물리 픽셀로 배치)
        public void PlaceToolbar()
        {
            IntPtr h = ToolbarHandle;
            OverlayWindow pri = Primary;
            if (h == IntPtr.Zero || pri == null) return;
            ScreenInfo s = pri.Screen;
            int x = s.Work.Left + (int)Math.Round(8 * s.Scale);
            int y = s.Work.Top + Math.Max(0, (int)Math.Round((s.Work.Height - toolbar.ActualHeight * s.Scale) / 2));
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        void EnsureToolbarOnScreen()
        {
            IntPtr h = ToolbarHandle;
            if (h != IntPtr.Zero && Native.MonitorFromWindow(h, 0 /* MONITOR_DEFAULTTONULL */) == IntPtr.Zero) PlaceToolbar();
        }

        // ───────────── 도구 / 속성 ─────────────
        public void SetTool(Tool t)
        {
            CurrentTool = t;
            ApplyTool();
            SetDrawMode(true);
        }

        public void SetColor(Color c)
        {
            CurrentColor = c;
            if (CurrentTool == Tool.Eraser) CurrentTool = Tool.Pen;
            ApplyTool();
            SetDrawMode(true);
        }

        public void SetSizeLevel(int level)
        {
            SizeLevel = level;
            ApplyTool();
            Notify();
        }

        void ApplyTool()
        {
            foreach (OverlayWindow o in Overlays) ApplyTool(o);
        }

        public void ApplyTool(OverlayWindow o)
        {
            DrawingAttributes da = new DrawingAttributes();
            da.Color = CurrentColor;
            da.IgnorePressure = true;
            da.FitToCurve = (CurrentTool == Tool.Pen || CurrentTool == Tool.Highlighter);
            da.StylusTip = StylusTip.Ellipse;
            if (CurrentTool == Tool.Highlighter)
            {
                da.IsHighlighter = true;
                da.Width = HighlighterSize;
                da.Height = HighlighterSize;
            }
            else
            {
                da.Width = PenSize;
                da.Height = PenSize;
            }
            o.ApplyTool(CurrentTool, CurrentColor, da);
        }

        // ───────────── 모드 전환 ─────────────
        public void SetDrawMode(bool on)
        {
            DrawMode = on;
            foreach (OverlayWindow o in Overlays) o.SetPassThrough(!on);
            if (on && Primary != null) Primary.Activate();
            Notify();
        }
        public void ToggleToolbar()
        {
            if (toolbar.IsVisible) toolbar.Hide();
            else { toolbar.Show(); EnsureToolbarOnScreen(); }
        }

        // ───────────── 실행 취소 / 다시 실행 / 지우기 ─────────────
        public void Record(StrokeCollection target, StrokeCollectionChangedEventArgs e)
        {
            if (suppressHistory) return;
            Change c = new Change();
            c.Target = target;
            c.Added = new StrokeCollection(e.Added);
            c.Removed = new StrokeCollection(e.Removed);
            UndoAction a = new UndoAction();
            a.Changes.Add(c);
            Push(a);
        }

        void Push(UndoAction a)
        {
            undoStack.Push(a);
            redoStack.Clear();
            Notify();
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            UndoAction a = undoStack.Pop();
            suppressHistory = true;
            for (int i = a.Changes.Count - 1; i >= 0; i--)
            {
                Change c = a.Changes[i];
                c.Target.Remove(c.Added);
                c.Target.Add(c.Removed);
            }
            suppressHistory = false;
            redoStack.Push(a);
            Notify();
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;
            UndoAction a = redoStack.Pop();
            suppressHistory = true;
            foreach (Change c in a.Changes)
            {
                c.Target.Remove(c.Removed);
                c.Target.Add(c.Added);
            }
            suppressHistory = false;
            undoStack.Push(a);
            Notify();
        }

        public void ClearAll()
        {
            UndoAction a = new UndoAction(); // 모든 모니터를 하나의 실행 취소 단위로 기록
            suppressHistory = true;
            foreach (OverlayWindow o in Overlays)
            {
                if (o.Ink.Strokes.Count == 0) continue;
                Change c = new Change();
                c.Target = o.Ink.Strokes;
                c.Added = new StrokeCollection();
                c.Removed = new StrokeCollection(o.Ink.Strokes);
                o.Ink.Strokes.Clear();
                a.Changes.Add(c);
            }
            suppressHistory = false;
            if (a.Changes.Count == 0) { Toast("지울 그림이 없습니다"); return; }
            Push(a);
            Toast("모두 지움  (Ctrl+Z 로 복구)");
        }

        public void Toast(string text)
        {
            OverlayWindow pri = Primary;
            if (pri != null) pri.Toast(text);
        }

        // ───────────── 로컬 단축키 ─────────────
        public void OnKey(object sender, KeyEventArgs e)
        {
            ModifierKeys m = Keyboard.Modifiers;
            bool ctrl = (m & ModifierKeys.Control) != 0;
            bool shift = (m & ModifierKeys.Shift) != 0;
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;

            if (ctrl && k == Key.Z && !shift) { Undo(); e.Handled = true; return; }
            if (ctrl && (k == Key.Y || (k == Key.Z && shift))) { Redo(); e.Handled = true; return; }
            if (k == Key.Escape) { SetDrawMode(false); Toast("마우스 통과 모드  (Ctrl+Shift+D 로 다시 그리기)"); e.Handled = true; return; }
            if (ctrl || (m & ModifierKeys.Alt) != 0) return;

            switch (k)
            {
                case Key.P: SetTool(Tool.Pen); break;
                case Key.H: SetTool(Tool.Highlighter); break;
                case Key.E: SetTool(Tool.Eraser); break;
                case Key.L: SetTool(Tool.Line); break;
                case Key.A: SetTool(Tool.Arrow); break;
                case Key.R: SetTool(Tool.Rect); break;
                case Key.O: SetTool(Tool.Ellipse); break;
                default:
                    if (k >= Key.D1 && k <= Key.D8) { SetColor(Palette[k - Key.D1]); break; }
                    return;
            }
            e.Handled = true;
        }

        void Notify()
        {
            if (ModelChanged != null) ModelChanged();
        }
    }

    // ───────────────────────── 모니터 한 대를 덮는 오버레이 (그리기 영역) ─────────────────────────
    class OverlayWindow : Window
    {
        readonly Board board;
        public ScreenInfo Screen { get; private set; }
        public InkCanvas Ink;

        Canvas overlayLayer;
        Polyline preview;
        Border toast;
        TextBlock toastText;
        DispatcherTimer toastTimer;

        bool shaping;
        Point shapeStart;
        bool penLine;     // 펜/형광펜을 Shift 누른 채 사용 중 (직선 모드)
        Tool shapeTool;

        // 터치 / 펜 입력 (한 번에 한 손가락/펜만 그린다)
        enum PointerMode { None, Ink, Erase, Shape }
        PointerMode pointerMode;
        uint activePointer;
        InkPresenter liveInk;           // 그리는 중인 선 (손을 떼면 Ink 로 옮김)
        Stroke liveStroke;
        IncrementalStrokeHitTester eraser;

        IntPtr hwnd;
        bool passThrough;
        bool detached;    // Board 가 닫은 경우 (종료 또는 모니터 제거)

        public OverlayWindow(Board board, ScreenInfo screen)
        {
            this.board = board;
            Screen = screen;
            Title = "ScreenPen";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 대략적인 초기 위치 — 정확한 위치는 창 핸들이 생긴 뒤 물리 픽셀로 맞춘다 (FitToScreen)
            Left = screen.Bounds.Left / screen.Scale;
            Top = screen.Bounds.Top / screen.Scale;
            Width = screen.Bounds.Width / screen.Scale;
            Height = screen.Bounds.Height / screen.Scale;
            passThrough = !board.DrawMode;

            Grid root = new Grid();
            Content = root;

            Ink = new InkCanvas();
            Ink.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // 거의 투명하지만 클릭은 받음
            Ink.UseCustomCursor = true;
            Ink.Cursor = Cursors.Cross;
            Ink.Strokes.StrokesChanged += delegate(object s, StrokeCollectionChangedEventArgs e) { board.Record(Ink.Strokes, e); };
            Ink.PreviewMouseLeftButtonDown += OnShapeDown;
            Ink.PreviewMouseMove += OnShapeMove;
            Ink.PreviewMouseLeftButtonUp += OnShapeUp;
            Ink.LostMouseCapture += delegate { CancelShape(); };
            root.Children.Add(Ink);

            liveInk = new InkPresenter();
            liveInk.IsHitTestVisible = false;
            liveInk.Strokes = new StrokeCollection();
            root.Children.Add(liveInk);

            overlayLayer = new Canvas();
            overlayLayer.IsHitTestVisible = false;
            root.Children.Add(overlayLayer);

            preview = new Polyline();
            preview.StrokeLineJoin = PenLineJoin.Round;
            preview.StrokeStartLineCap = PenLineCap.Round;
            preview.StrokeEndLineCap = PenLineCap.Round;
            overlayLayer.Children.Add(preview);

            toastText = new TextBlock();
            toastText.Foreground = Brushes.White;
            toastText.FontSize = 14;
            toastText.FontFamily = new FontFamily("Malgun Gothic, Segoe UI");
            toast = new Border();
            toast.Background = new SolidColorBrush(Color.FromArgb(230, 28, 28, 30));
            toast.CornerRadius = new CornerRadius(10);
            toast.Padding = new Thickness(16, 9, 16, 9);
            toast.Child = toastText;
            toast.Opacity = 0;
            overlayLayer.Children.Add(toast);

            toastTimer = new DispatcherTimer();
            toastTimer.Interval = TimeSpan.FromMilliseconds(1800);
            toastTimer.Tick += delegate
            {
                toastTimer.Stop();
                toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            };

            PreviewKeyDown += board.OnKey;
            Activated += delegate { board.RaiseToolbar(); };
            Loaded += delegate { FitToScreen(); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW;
            ex = passThrough ? (ex | Native.WS_EX_TRANSPARENT) : (ex & ~Native.WS_EX_TRANSPARENT);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
            HwndSource.FromHwnd(hwnd).AddHook(PointerHook);
            DisableTouchFeedback();
            FitToScreen();
        }

        // 터치/펜을 꾹 누를 때 나오는 원·탭 표시 등 시스템 효과를 끈다 (그리는 선이 곧 피드백)
        void DisableTouchFeedback()
        {
            try
            {
                int off = 0;
                for (int f = 1; f <= 11; f++) Native.SetWindowFeedbackSetting(hwnd, f, 0, 4, ref off);
            }
            catch (EntryPointNotFoundException) { }
        }

        // 다른 배율의 모니터로 옮겨지면 WPF 가 창 크기를 배율에 맞춰 바꾸므로 모니터 크기로 되돌린다
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitToScreen));
            board.ApplyTool(this); // 커서 크기 갱신
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (!detached) board.Quit(); // Alt+F4 등으로 직접 닫으면 프로그램 종료
        }

        public void Detach()
        {
            detached = true;
            Close();
        }

        public void SetScreen(ScreenInfo s)
        {
            Screen = s;
            FitToScreen();
        }

        void FitToScreen()
        {
            if (hwnd == IntPtr.Zero) return;
            Native.RECT r = Screen.Bounds;
            Native.SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        public void SetPassThrough(bool on)
        {
            passThrough = on;
            if (on) { CancelPointer(); CancelShape(); }
            if (hwnd == IntPtr.Zero) return;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            ex = on ? (ex | Native.WS_EX_TRANSPARENT) : (ex & ~Native.WS_EX_TRANSPARENT);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
        }

        // 화면 배율에 맞춰 커서 크기를 정한다 (100% → 32px, 250% → 약 53px)
        int CursorSize()
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return (int)Math.Max(32, Math.Min(64, Math.Round(32 + (scale - 1) * 14)));
        }

        public void ApplyTool(Tool tool, Color color, DrawingAttributes da)
        {
            CancelPointer();
            CancelShape();
            Ink.DefaultDrawingAttributes = da.Clone();
            switch (tool)
            {
                case Tool.Eraser:
                    Ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
                    Ink.Cursor = CursorFactory.Get(tool, color, CursorSize());
                    break;
                case Tool.Pen:
                case Tool.Highlighter:
                    Ink.EditingMode = InkCanvasEditingMode.Ink;
                    Ink.Cursor = CursorFactory.Get(tool, color, CursorSize());
                    break;
                default:
                    Ink.EditingMode = InkCanvasEditingMode.None;
                    Ink.Cursor = Cursors.Cross;
                    break;
            }
        }

        // ───────────── 도형 그리기 (직선 / 화살표 / 사각형 / 원) + Shift 누른 채 펜 = 직선 ─────────────
        bool ShiftDown { get { return (Keyboard.Modifiers & ModifierKeys.Shift) != 0; } }

        void OnShapeDown(object sender, MouseButtonEventArgs e)
        {
            if (pointerMode != PointerMode.None || !BeginShape(e.GetPosition(Ink))) return;
            Ink.CaptureMouse();
            e.Handled = true; // InkCanvas 가 자유 곡선을 그리지 못하게 막음
        }

        void OnShapeMove(object sender, MouseEventArgs e)
        {
            if (shaping && Ink.IsMouseCaptured) MoveShape(e.GetPosition(Ink));
        }

        void OnShapeUp(object sender, MouseButtonEventArgs e)
        {
            if (!shaping || !Ink.IsMouseCaptured) return;
            e.Handled = true;
            EndShape(e.GetPosition(Ink));
        }

        // 도형 도구이거나 Shift+펜이면 도형 그리기를 시작한다 (마우스·터치 공용)
        bool BeginShape(Point start)
        {
            if (!board.DrawMode) return false;
            Tool tool = board.CurrentTool;
            bool freehand = tool == Tool.Pen || tool == Tool.Highlighter;
            if (!board.IsShapeTool && !(freehand && ShiftDown)) return false;

            penLine = !board.IsShapeTool;
            shapeTool = penLine ? Tool.Line : tool;
            shaping = true;
            shapeStart = start;
            Color c = board.CurrentColor;
            if (tool == Tool.Highlighter)
            {
                preview.Stroke = new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B));
                preview.StrokeThickness = board.HighlighterSize;
            }
            else
            {
                preview.Stroke = new SolidColorBrush(c);
                preview.StrokeThickness = board.PenSize;
            }
            preview.Points = new PointCollection();
            return true;
        }

        List<Point> CurrentShape(Point end)
        {
            if (penLine) return BuildShape(Tool.Line, shapeStart, SoftSnap(shapeStart, end), false);
            return BuildShape(shapeTool, shapeStart, end, ShiftDown);
        }

        // 거의 수평/수직(약 4도 이내)이면 정확히 맞춰 준다
        static Point SoftSnap(Point a, Point b)
        {
            double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
            double k = Math.Tan(4 * Math.PI / 180);
            if (dy < dx * k) return new Point(b.X, a.Y);
            if (dx < dy * k) return new Point(a.X, b.Y);
            return b;
        }

        void MoveShape(Point p)
        {
            if (!shaping) return;
            preview.Points = new PointCollection(CurrentShape(p));
        }

        void EndShape(Point end)
        {
            if (!shaping) return;
            List<Point> pts = CurrentShape(end);
            CancelShape();
            if ((end - shapeStart).Length < 3) return;
            Stroke s = new Stroke(new StylusPointCollection(pts));
            s.DrawingAttributes = Ink.DefaultDrawingAttributes.Clone();
            s.DrawingAttributes.FitToCurve = false;
            Ink.Strokes.Add(s);
        }
        void CancelShape()
        {
            if (!shaping) return;
            shaping = false;
            preview.Points = new PointCollection();
            if (Ink.IsMouseCaptured) Ink.ReleaseMouseCapture();
        }

        List<Point> BuildShape(Tool t, Point a, Point b, bool constrain)
        {
            List<Point> pts = new List<Point>();
            double dx = b.X - a.X, dy = b.Y - a.Y;

            if (t == Tool.Line || t == Tool.Arrow)
            {
                if (constrain) // 45도 단위로 고정
                {
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                    dx = Math.Cos(ang) * len; dy = Math.Sin(ang) * len;
                    b = new Point(a.X + dx, a.Y + dy);
                }
                pts.Add(a);
                pts.Add(b);
                if (t == Tool.Arrow)
                {
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    double head = Math.Min(Math.Max(16, board.PenSize * 4.5), len * 0.6);
                    double ang = Math.Atan2(dy, dx);
                    double spread = 28 * Math.PI / 180;
                    Point l = new Point(b.X - head * Math.Cos(ang - spread), b.Y - head * Math.Sin(ang - spread));
                    Point r = new Point(b.X - head * Math.Cos(ang + spread), b.Y - head * Math.Sin(ang + spread));
                    pts.Add(l); pts.Add(b); pts.Add(r);
                }
                return pts;
            }

            if (constrain) // 정사각형 / 정원
            {
                double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                dx = Math.Sign(dx) * side; dy = Math.Sign(dy) * side;
                b = new Point(a.X + dx, a.Y + dy);
            }

            if (t == Tool.Rect)
            {
                pts.Add(a);
                pts.Add(new Point(b.X, a.Y));
                pts.Add(b);
                pts.Add(new Point(a.X, b.Y));
                pts.Add(a);
            }
            else
            {
                double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2;
                double rx = Math.Abs(dx) / 2, ry = Math.Abs(dy) / 2;
                const int N = 96;
                for (int i = 0; i <= N; i++)
                {
                    double th = 2 * Math.PI * i / N;
                    pts.Add(new Point(cx + rx * Math.Cos(th), cy + ry * Math.Sin(th)));
                }
            }
            return pts;
        }

        // ───────────── 터치 / 펜 (WM_POINTER 직접 처리) ─────────────
        // WM_POINTER 의 좌표는 화면 물리 픽셀이다. 이 창의 실제 픽셀 크기와 WPF 크기의 비율로 환산하므로
        // 모니터마다 해상도·배율이 달라도, 창을 다른 모니터로 옮겨도 터치한 지점에 그려진다.
        IntPtr PointerHook(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != Native.WM_POINTERDOWN && msg != Native.WM_POINTERUPDATE &&
                msg != Native.WM_POINTERUP && msg != Native.WM_POINTERCAPTURECHANGED) return IntPtr.Zero;

            long wp = wParam.ToInt64();
            uint id = (uint)(wp & 0xFFFF);
            uint flags = (uint)((wp >> 16) & 0xFFFF);
            bool mine = pointerMode != PointerMode.None && id == activePointer;

            if (msg == Native.WM_POINTERCAPTURECHANGED)
            {
                if (mine) CancelPointer();
                return IntPtr.Zero;
            }

            uint type;
            if (!Native.GetPointerType(id, out type) || (type != Native.PT_TOUCH && type != Native.PT_PEN)) return IntPtr.Zero;
            if (!board.DrawMode) return IntPtr.Zero;
            // 펜을 화면에 대지 않고 띄워 둔 상태는 마우스로 넘겨 커서가 펜을 따라오게 한다
            if (!mine && msg == Native.WM_POINTERUPDATE && (flags & Native.POINTER_FLAG_INCONTACT) == 0) return IntPtr.Zero;

            handled = true; // WPF 로 넘기지 않음 (InkCanvas 가 어긋난 좌표로 한 번 더 그리는 것 방지)

            long lp = lParam.ToInt64();
            Point p = ScreenToInk((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));

            if (msg == Native.WM_POINTERDOWN)
            {
                if (pointerMode == PointerMode.None) BeginPointer(id, p); // 두 번째 손가락 등은 무시
            }
            else if (mine && (flags & Native.POINTER_FLAG_CANCELED) != 0) CancelPointer();
            else if (mine && msg == Native.WM_POINTERUPDATE) MovePointer(p);
            else if (mine && msg == Native.WM_POINTERUP) EndPointer(p);
            return IntPtr.Zero;
        }

        // 화면 물리 픽셀 → Ink 좌표(DIP)
        Point ScreenToInk(int x, int y)
        {
            Native.POINT pt = new Native.POINT();
            pt.X = x; pt.Y = y;
            Native.ScreenToClient(hwnd, ref pt);
            Native.RECT rc;
            Native.GetClientRect(hwnd, out rc);
            double sx = rc.Width > 0 && Ink.ActualWidth > 0 ? Ink.ActualWidth / rc.Width : 1 / VisualTreeHelper.GetDpi(this).DpiScaleX;
            double sy = rc.Height > 0 && Ink.ActualHeight > 0 ? Ink.ActualHeight / rc.Height : 1 / VisualTreeHelper.GetDpi(this).DpiScaleY;
            return new Point(pt.X * sx, pt.Y * sy);
        }

        void BeginPointer(uint id, Point p)
        {
            activePointer = id;
            if (BeginShape(p)) { pointerMode = PointerMode.Shape; return; }

            if (board.CurrentTool == Tool.Eraser)
            {
                pointerMode = PointerMode.Erase;
                eraser = Ink.Strokes.GetIncrementalStrokeHitTester(Ink.EraserShape);
                eraser.StrokeHit += delegate(object s, StrokeHitEventArgs e) { Ink.Strokes.Remove(e.HitStroke); };
                eraser.AddPoint(p);
                return;
            }

            pointerMode = PointerMode.Ink;
            DrawingAttributes da = Ink.DefaultDrawingAttributes.Clone();
            da.FitToCurve = false; // 그리는 동안은 빠르게, 손을 떼면 원래 설정(곡선 보정)으로
            liveStroke = new Stroke(new StylusPointCollection(new Point[] { p }), da);
            liveInk.Strokes.Add(liveStroke);
        }

        void MovePointer(Point p)
        {
            switch (pointerMode)
            {
                case PointerMode.Shape: MoveShape(p); break;
                case PointerMode.Erase: eraser.AddPoint(p); break;
                case PointerMode.Ink:
                    StylusPointCollection pts = liveStroke.StylusPoints;
                    StylusPoint last = pts[pts.Count - 1];
                    if (Math.Abs(last.X - p.X) < 0.5 && Math.Abs(last.Y - p.Y) < 0.5) return;
                    pts.Add(new StylusPoint(p.X, p.Y));
                    break;
            }
        }

        void EndPointer(Point p)
        {
            PointerMode mode = pointerMode;
            if (mode == PointerMode.Ink) MovePointer(p);
            else if (mode == PointerMode.Erase) eraser.AddPoint(p);

            if (mode == PointerMode.Shape) { pointerMode = PointerMode.None; EndShape(p); return; }
            if (mode == PointerMode.Ink)
            {
                Stroke s = liveStroke;
                ResetPointer();
                s.DrawingAttributes = Ink.DefaultDrawingAttributes.Clone();
                Ink.Strokes.Add(s); // 여기서 실행 취소 기록
                return;
            }
            ResetPointer();
        }

        void CancelPointer()
        {
            if (pointerMode == PointerMode.Shape) CancelShape();
            ResetPointer();
        }

        void ResetPointer()
        {
            if (eraser != null) { eraser.EndHitTesting(); eraser = null; }
            if (liveStroke != null) { liveInk.Strokes.Remove(liveStroke); liveStroke = null; }
            pointerMode = PointerMode.None;
        }

        // ───────────── 안내 메시지 (이 모니터의 작업 영역 아래쪽 가운데) ─────────────
        public void Toast(string text)
        {
            toastText.Text = text;
            toast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = toast.DesiredSize.Width;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            Native.RECT b = Screen.Bounds, wa = Screen.Work;
            double left = (wa.Left - b.Left) / dpi.DpiScaleX;
            double right = (wa.Right - b.Left) / dpi.DpiScaleX;
            double bottom = (wa.Bottom - b.Top) / dpi.DpiScaleY;
            Canvas.SetLeft(toast, (left + right) / 2 - w / 2);
            Canvas.SetTop(toast, bottom - 120);
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            toastTimer.Stop();
            toastTimer.Start();
        }
    }

    // ───────────────────────── 도구별 커서 (펜 / 형광펜 / 지우개) ─────────────────────────
    static class CursorFactory
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
        [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr hCursor);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

        sealed class CursorHandle : SafeHandle
        {
            public CursorHandle(IntPtr h) : base(IntPtr.Zero, true) { SetHandle(h); }
            public override bool IsInvalid { get { return handle == IntPtr.Zero; } }
            protected override bool ReleaseHandle() { return DestroyCursor(handle); }
        }

        static readonly Dictionary<string, Cursor> cache = new Dictionary<string, Cursor>();

        // 24x24 격자 기준 아이콘 모양과 끝점(핫스팟). 도구 모음 아이콘과 동일.
        public static Cursor Get(Tool tool, Color color, int size)
        {
            string shape; double hx, hy; Brush fill;
            if (tool == Tool.Highlighter)
            {
                shape = "M7 15 L15 7 L18 10 L10 18 Z M7 15 L5 20 L10 18 M14 21 H21";
                hx = 5; hy = 20;
                fill = new SolidColorBrush(Color.FromArgb(235, color.R, color.G, color.B));
            }
            else if (tool == Tool.Eraser)
            {
                shape = "M4 15 L12 7 L18 13 L12 19 H8 Z M9 10 L15 16 M12 19 H21";
                hx = 10; hy = 19;
                fill = new SolidColorBrush(Color.FromRgb(236, 236, 240));
            }
            else
            {
                shape = "M4 20 L5 15 L16 4 L20 8 L9 19 Z M13 7 L17 11";
                hx = 4; hy = 20;
                fill = new SolidColorBrush(color);
            }

            string key = tool + "|" + (tool == Tool.Eraser ? "" : color.ToString()) + "|" + size;
            Cursor cached;
            if (cache.TryGetValue(key, out cached)) return cached;

            Cursor made = null;
            try { made = Build(shape, hx, hy, fill, size); } catch { }
            if (made == null) made = (tool == Tool.Eraser) ? Cursors.Hand : Cursors.Cross;
            cache[key] = made;
            return made;
        }

        static Cursor Build(string shape, double hx, double hy, Brush fill, int size)
        {
            double s = size / 26.0, off = s; // 격자 1칸 여백
            Geometry g = Geometry.Parse(shape);
            Pen outline = new Pen(Brushes.Black, 3.4);
            outline.LineJoin = PenLineJoin.Round; outline.StartLineCap = PenLineCap.Round; outline.EndLineCap = PenLineCap.Round;
            Pen inner = new Pen(Brushes.White, 1.5);
            inner.LineJoin = PenLineJoin.Round; inner.StartLineCap = PenLineCap.Round; inner.EndLineCap = PenLineCap.Round;

            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(new MatrixTransform(s, 0, 0, s, off, off));
                dc.DrawGeometry(null, outline, g);
                dc.DrawGeometry(fill, inner, g);
                dc.Pop();
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            FormatConvertedBitmap straight = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
            int stride = size * 4;
            byte[] px = new byte[stride * size];
            straight.CopyPixels(px, stride, 0);

            GCHandle pin = GCHandle.Alloc(px, GCHandleType.Pinned);
            IntPtr hIcon;
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(size, size, stride,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject()))
                {
                    hIcon = bmp.GetHicon();
                }
            }
            finally { pin.Free(); }

            ICONINFO ii;
            if (!GetIconInfo(hIcon, out ii)) { DestroyIcon(hIcon); return null; }
            ii.fIcon = false;
            ii.xHotspot = (int)Math.Round(hx * s + off);
            ii.yHotspot = (int)Math.Round(hy * s + off);
            IntPtr hCursor = CreateIconIndirect(ref ii);
            DeleteObject(ii.hbmColor);
            DeleteObject(ii.hbmMask);
            DestroyIcon(hIcon);
            if (hCursor == IntPtr.Zero) return null;
            return CursorInteropHelper.Create(new CursorHandle(hCursor));
        }
    }
    // ───────────────────────── 아이콘 버튼 ─────────────────────────
    class IconButton : Border
    {
        public static readonly Brush Idle = Brushes.Transparent;
        public static readonly Brush Hover = new SolidColorBrush(Color.FromRgb(58, 58, 62));
        public static readonly Brush Press = new SolidColorBrush(Color.FromRgb(78, 78, 84));
        public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(10, 132, 255));

        readonly System.Windows.Shapes.Path path;
        bool active, enabled = true, over, down;
        public event Action Click;

        public IconButton(string data, string tip)
        {
            Width = 32; Height = 32;
            CornerRadius = new CornerRadius(8);
            Margin = new Thickness(0, 1, 0, 1);
            Background = Idle;
            Cursor = Cursors.Hand;
            ToolTip = tip;
            ToolTipService.SetInitialShowDelay(this, 350);

            path = new System.Windows.Shapes.Path();
            path.Data = Geometry.Parse(data);
            path.StrokeThickness = 1.8;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            Canvas c = new Canvas { Width = 24, Height = 24 };
            c.Children.Add(path);
            Viewbox vb = new Viewbox { Width = 18, Height = 18, Child = c };
            Child = vb;

            MouseEnter += delegate { over = true; Refresh(); };
            MouseLeave += delegate { over = false; down = false; Refresh(); };
            MouseLeftButtonDown += delegate { if (enabled) { down = true; Refresh(); } };
            MouseLeftButtonUp += delegate
            {
                bool fire = down && enabled;
                down = false; Refresh();
                if (fire && Click != null) Click();
            };
            Refresh();
        }

        public bool Active { get { return active; } set { active = value; Refresh(); } }
        public bool Enabled { get { return enabled; } set { enabled = value; Refresh(); } }
        public string Tip { set { ToolTip = value; } }

        void Refresh()
        {
            path.Stroke = new SolidColorBrush(!enabled ? Color.FromRgb(88, 88, 92) : (active ? Colors.White : Color.FromRgb(229, 229, 234)));
            if (active) Background = Accent;
            else if (down && enabled) Background = Press;
            else if (over && enabled) Background = Hover;
            else Background = Idle;
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        }
    }

    // ───────────────────────── 색상 칩 ─────────────────────────
    class ColorChip : Grid
    {
        readonly Ellipse ring;
        public Color Color;
        public event Action Click;

        public ColorChip(Color c)
        {
            Color = c;
            Width = 17; Height = 17;
            Cursor = Cursors.Hand;
            Margin = new Thickness(0, 1, 0, 1);
            Background = Brushes.Transparent;

            ring = new Ellipse { Width = 16, Height = 16, StrokeThickness = 2, Stroke = Brushes.Transparent };
            Children.Add(ring);
            Ellipse dot = new Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(c) };
            dot.Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            dot.StrokeThickness = 1;
            Children.Add(dot);
            MouseLeftButtonUp += delegate { if (Click != null) Click(); };
        }

        public bool Selected
        {
            set { ring.Stroke = value ? Brushes.White : Brushes.Transparent; }
        }
    }

    // ───────────────────────── 굵기 버튼 (얇게 / 보통 / 굵게) ─────────────────────────
    class SizeButton : Border
    {
        readonly Border line;
        bool active, over, enabled = true;
        public event Action Click;

        public SizeButton(double thickness, string tip)
        {
            Width = 32; Height = 24;
            CornerRadius = new CornerRadius(7);
            Margin = new Thickness(0, 1, 0, 1);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            ToolTip = tip;
            line = new Border { Width = 18, Height = thickness, CornerRadius = new CornerRadius(thickness / 2), Background = Brushes.White };
            line.HorizontalAlignment = HorizontalAlignment.Center;
            line.VerticalAlignment = VerticalAlignment.Center;
            Child = line;
            MouseEnter += delegate { over = true; Refresh(); };
            MouseLeave += delegate { over = false; Refresh(); };
            MouseLeftButtonUp += delegate { if (enabled && Click != null) Click(); };
            Refresh();
        }

        public bool Active { set { active = value; Refresh(); } }
        public bool Enabled { set { enabled = value; Refresh(); } }

        void Refresh()
        {
            Background = active && enabled ? IconButton.Accent : (over && enabled ? IconButton.Hover : Brushes.Transparent);
            line.Opacity = enabled ? 1 : 0.3;
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        }
    }
    // ───────────────────────── 도구 모음 (세로) ─────────────────────────
    class ToolbarWindow : Window
    {
        const string P_PEN = "M4 20 L5 15 L16 4 L20 8 L9 19 Z M13 7 L17 11";
        const string P_HIGH = "M7 15 L15 7 L18 10 L10 18 Z M7 15 L5 20 L10 18 M14 21 H21";
        const string P_ERASE = "M4 15 L12 7 L18 13 L12 19 H8 Z M9 10 L15 16 M12 19 H21";
        const string P_LINE = "M5 19 L19 5";
        const string P_ARROW = "M5 19 L19 5 M9 5 H19 V15";
        const string P_RECT = "M4 6 H20 V18 H4 Z";
        const string P_ELLIPSE = "M3 12 A9 7 0 1 1 21 12 A9 7 0 1 1 3 12 Z";
        const string P_CURSOR = "M6 3 L6 19 L10.5 15 L13.5 21 L16 20 L13 14 L19 14 Z";
        const string P_UNDO = "M9 6 L4 11 L9 16 M4 11 H15 A5 5 0 0 1 15 21 H10";
        const string P_REDO = "M15 6 L20 11 L15 16 M20 11 H9 A5 5 0 0 0 9 21 H14";
        const string P_TRASH = "M4 7 H20 M9 7 V4 H15 V7 M6 7 L7 20 H17 L18 7 M10 11 V16 M14 11 V16";
        const string P_CLOSE = "M6 6 L18 18 M18 6 L6 18";

        readonly Board ov;
        readonly Dictionary<Tool, IconButton> toolButtons = new Dictionary<Tool, IconButton>();
        readonly List<ColorChip> chips = new List<ColorChip>();
        IconButton btnMouse, btnUndo, btnRedo;
        readonly SizeButton[] sizeButtons = new SizeButton[3];

        public ToolbarWindow(Board overlay)
        {
            ov = overlay;
            Title = "ScreenPen 도구 모음";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI");

            StackPanel bar = new StackPanel { Orientation = Orientation.Vertical };

            // 이동 손잡이
            Border grip = new Border { Width = 32, Height = 18, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "드래그해서 이동" };
            System.Windows.Controls.Primitives.UniformGrid dots = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 3, Width = 16, Height = 10 };
            for (int i = 0; i < 6; i++)
                dots.Children.Add(new Ellipse { Width = 3, Height = 3, Fill = new SolidColorBrush(Color.FromRgb(110, 110, 116)) });
            grip.Child = dots;
            grip.MouseLeftButtonDown += delegate { DragMove(); };
            bar.Children.Add(grip);

            // 1) 마우스, 펜, 형광펜, 지우개, 전체 삭제
            btnMouse = AddButton(bar, P_CURSOR, "마우스 (Esc / Ctrl+Shift+D)\n그림은 남기고 뒤쪽 프로그램을 조작합니다", delegate { ov.SetDrawMode(!ov.DrawMode); });
            AddTool(bar, Tool.Pen, P_PEN, "펜 (P)");
            AddTool(bar, Tool.Highlighter, P_HIGH, "형광펜 (H)");
            AddTool(bar, Tool.Eraser, P_ERASE, "지우개 (E) - 선을 클릭/드래그해서 삭제");
            AddButton(bar, P_TRASH, "전체 삭제 (Ctrl+Shift+C)", delegate { ov.ClearAll(); });
            AddSep(bar);

            // 2) 도형
            AddTool(bar, Tool.Line, P_LINE, "직선 (L)  Shift: 각도 고정");
            AddTool(bar, Tool.Arrow, P_ARROW, "화살표 (A)  Shift: 각도 고정");
            AddTool(bar, Tool.Rect, P_RECT, "사각형 (R)  Shift: 정사각형");
            AddTool(bar, Tool.Ellipse, P_ELLIPSE, "원 (O)  Shift: 정원");
            AddSep(bar);

            // 3) 색상 (2열)
            System.Windows.Controls.Primitives.UniformGrid palette = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < Board.Palette.Length; i++)
            {
                ColorChip chip = new ColorChip(Board.Palette[i]);
                Color c = Board.Palette[i];
                chip.Margin = new Thickness(0);
                chip.ToolTip = "색상 (" + (i + 1) + ")";
                chip.Click += delegate { ov.SetColor(c); };
                chips.Add(chip);
                palette.Children.Add(chip);
            }
            bar.Children.Add(palette);
            AddSep(bar);

            // 4) 굵기 (3단계)
            string[] sizeTips = new string[] { "얇게", "보통", "굵게" };
            double[] sizeLines = new double[] { 2, 4, 7 };
            for (int i = 0; i < 3; i++)
            {
                int level = i;
                SizeButton sb = new SizeButton(sizeLines[i], sizeTips[i]);
                sb.Click += delegate { ov.SetSizeLevel(level); };
                sizeButtons[i] = sb;
                bar.Children.Add(sb);
            }
            AddSep(bar);

            // 5) 편집 / 종료
            btnUndo = AddButton(bar, P_UNDO, "실행 취소 (Ctrl+Z)", delegate { ov.Undo(); });
            btnRedo = AddButton(bar, P_REDO, "다시 실행 (Ctrl+Y)", delegate { ov.Redo(); });
            AddSep(bar);
            AddButton(bar, P_CLOSE, "종료 (Ctrl+Shift+Q)", delegate { ov.Quit(); });

            Border frame = new Border();
            frame.Background = new SolidColorBrush(Color.FromArgb(240, 28, 28, 30));
            frame.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(10);
            frame.Padding = new Thickness(2, 0, 2, 0); // 양옆 2px
            frame.Margin = new Thickness(0);
            frame.HorizontalAlignment = HorizontalAlignment.Left; // 창 최소 폭(OS 강제)보다 막대가 좁으므로 나머지는 투명 영역
            frame.Child = bar;
            Content = frame;

            PreviewKeyDown += ov.OnKey;
            ov.ModelChanged += RefreshState;
            Loaded += delegate
            {
                ov.PlaceToolbar();
                RefreshState();
            };
        }

        void AddTool(Panel parent, Tool t, string data, string tip)
        {
            IconButton b = new IconButton(data, tip);
            Tool tool = t;
            b.Click += delegate { ov.SetTool(tool); };
            toolButtons[t] = b;
            parent.Children.Add(b);
        }

        IconButton AddButton(Panel parent, string data, string tip, Action click)
        {
            IconButton b = new IconButton(data, tip);
            b.Click += click;
            parent.Children.Add(b);
            return b;
        }

        void AddSep(Panel parent)
        {
            parent.Children.Add(new Border
            {
                Width = 24, Height = 1,
                Margin = new Thickness(0, 3, 0, 3),
                Background = new SolidColorBrush(Color.FromRgb(62, 62, 66))
            });
        }

        void RefreshState()
        {
            foreach (KeyValuePair<Tool, IconButton> kv in toolButtons)
                kv.Value.Active = ov.DrawMode && kv.Key == ov.CurrentTool;

            foreach (ColorChip c in chips)
                c.Selected = ov.CurrentTool != Tool.Eraser && c.Color == ov.CurrentColor;

            bool eraser = ov.CurrentTool == Tool.Eraser;
            for (int i = 0; i < 3; i++)
            {
                sizeButtons[i].Enabled = !eraser;
                sizeButtons[i].Active = !eraser && i == ov.SizeLevel;
            }

            btnUndo.Enabled = ov.CanUndo;
            btnRedo.Enabled = ov.CanRedo;
            btnMouse.Active = !ov.DrawMode;
        }
    }
}