// 东方变速器 - Touhou Project single-player practice speed controller
// Copyright (C) 2026 StarLorentz
// Licensed under GNU GPL v3.0. See LICENSE for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("东方变速器")]
[assembly: AssemblyDescription("东方Project单机弹幕练习用时间控制工具")]
[assembly: AssemblyCompany("StarLorentz")]
[assembly: AssemblyProduct("东方变速器")]
[assembly: AssemblyCopyright("Copyright © 2026 StarLorentz")]
[assembly: AssemblyVersion("0.1.1.0")]
[assembly: AssemblyFileVersion("0.1.1.0")]

namespace TouhouSpeedController
{
    internal sealed class GameProcess
    {
        public Process Process;
        public bool Is64Bit;
        public string Display;
        public override string ToString() { return Display; }
    }

    internal sealed class BridgeClient : IDisposable
    {
        private readonly string pipeName;
        private NamedPipeClientStream pipe;
        private StreamReader reader;
        private StreamWriter writer;

        public BridgeClient(string pipeName) { this.pipeName = pipeName; }

        public string Command(string command)
        {
            EnsureConnected();
            try
            {
                writer.WriteLine(command);
                string response = reader.ReadLine();
                if (response == null) throw new IOException("变速后端没有返回结果");
                return response.Trim();
            }
            catch
            {
                Close();
                throw;
            }
        }

        private void EnsureConnected()
        {
            if (pipe != null && pipe.IsConnected) return;
            Close();
            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(2500);
            reader = new StreamReader(pipe, Encoding.UTF8, false, 256);
            writer = new StreamWriter(pipe, new UTF8Encoding(false), 256);
            writer.AutoFlush = true;
        }

        private void Close()
        {
            try { if (writer != null) writer.Dispose(); } catch { }
            try { if (reader != null) reader.Dispose(); } catch { }
            try { if (pipe != null) pipe.Dispose(); } catch { }
            writer = null;
            reader = null;
            pipe = null;
        }

        public void Dispose() { Close(); }
    }

    internal sealed class SpeedBackend : IDisposable
    {
        private readonly string appDir;
        private Process bridge32;
        private Process bridge64;
        private BridgeClient client32;
        private BridgeClient client64;

        public SpeedBackend()
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            RequireFile("bridge32.exe");
            RequireFile("bridge64.exe");
            RequireFile("speedpatch32.dll");
            RequireFile("speedpatch64.dll");
            bridge32 = StartBridge("bridge32.exe");
            bridge64 = StartBridge("bridge64.exe");
            client32 = new BridgeClient("OpenSpeedyBridge32");
            client64 = new BridgeClient("OpenSpeedyBridge64");
            WaitUntilReady();
        }

        private void RequireFile(string name)
        {
            if (!File.Exists(Path.Combine(appDir, name)))
                throw new FileNotFoundException("缺少必要文件：" + name);
        }

        private Process StartBridge(string name)
        {
            ProcessStartInfo info = new ProcessStartInfo(Path.Combine(appDir, name));
            info.WorkingDirectory = appDir;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            return Process.Start(info);
        }

        private void WaitUntilReady()
        {
            Exception last = null;
            for (int i = 0; i < 25; i++)
            {
                try
                {
                    if (client32.Command("GETSPEED").StartsWith("OK") &&
                        client64.Command("GETSPEED").StartsWith("OK")) return;
                }
                catch (Exception ex) { last = ex; }
                Thread.Sleep(100);
            }
            throw new IOException("变速后端启动失败", last);
        }

        private static void EnsureOk(string command, string response)
        {
            if (!response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(command + " 失败：" + response);
        }

        public void Apply(Process process, bool is64Bit, decimal fps)
        {
            double factor = (double)fps / 60.0;
            string factorText = factor.ToString("0.########", CultureInfo.InvariantCulture);
            EnsureOk("设置 32 位速度", client32.Command("SETSPEED " + factorText));
            EnsureOk("设置 64 位速度", client64.Command("SETSPEED " + factorText));
            BridgeClient target = is64Bit ? client64 : client32;
            string pid = process.Id.ToString(CultureInfo.InvariantCulture);
            string injected = target.Command("INJECT " + pid);
            EnsureOk("注入游戏", injected);
            EnsureOk("启用变速", target.Command("ENABLE " + pid));
        }

        public void Restore()
        {
            EnsureOk("恢复 32 位速度", client32.Command("SETSPEED 1"));
            EnsureOk("恢复 64 位速度", client64.Command("SETSPEED 1"));
        }

        public void Dispose()
        {
            try { Restore(); } catch { }
            try { client32.Command("SHUTDOWN"); } catch { }
            try { client64.Command("SHUTDOWN"); } catch { }
            if (client32 != null) client32.Dispose();
            if (client64 != null) client64.Dispose();
            try { if (bridge32 != null && !bridge32.HasExited) bridge32.WaitForExit(1000); } catch { }
            try { if (bridge64 != null && !bridge64.HasExited) bridge64.WaitForExit(1000); } catch { }
            try { if (bridge32 != null && !bridge32.HasExited) bridge32.Kill(); } catch { }
            try { if (bridge64 != null && !bridge64.HasExited) bridge64.Kill(); } catch { }
        }
    }

    internal sealed class MainForm : Form
    {
        private const int FocusHotkeyId = 0x5453;
        private const int WmHotkey = 0x0312;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModNoRepeat = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private readonly ComboBox processBox = new ComboBox();
        private readonly NumericUpDown fpsBox = new NumericUpDown();
        private readonly Label status = new Label();
        private readonly Button apply = new Button();
        private readonly Button restore = new Button();
        private readonly Button refresh = new Button();
        private readonly Label practiceSupport = new Label();
        private Label practiceLivesLabel, practicePowerLabel;
        private readonly NumericUpDown practiceLives = new NumericUpDown();
        private readonly NumericUpDown practiceBombs = new NumericUpDown();
        private readonly NumericUpDown practiceBombFragments = new NumericUpDown();
        private readonly NumericUpDown practicePower = new NumericUpDown();
        private readonly Button readPractice = new Button();
        private readonly Button applyPracticeAll = new Button();
        private readonly Button applyPracticeLives = new Button();
        private readonly Button applyPracticeBombs = new Button();
        private readonly Button applyPracticeBombFragments = new Button();
        private readonly Button applyPracticePower = new Button();
        private readonly CheckBox infiniteLives = new CheckBox();
        private readonly CheckBox infiniteBombs = new CheckBox();
        private readonly CheckBox invincibility = new CheckBox();
        private readonly CheckBox lockPower = new CheckBox();
        private readonly System.Windows.Forms.Timer powerSyncTimer = new System.Windows.Forms.Timer();
        private PowerSyncSession powerSync;
        private bool updatingPowerLock, powerRequestPending;
        private int memoryPowerTarget;
        private readonly System.Windows.Forms.Timer practiceFreezeTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer focusHotkeyFallbackTimer = new System.Windows.Forms.Timer();
        private InvincibilityPatch activeInvincibility;
        private SpeedBackend backend;
        private volatile bool closing;
        private bool updatingInfiniteChecks;
        private bool focusHotkeyRegistered;
        private bool focusHotkeyHeld;

        public MainForm()
        {
            Text = "东方变速器";
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(540, 568);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            string appImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app-image.png");
            if (File.Exists(appImagePath))
            {
                PictureBox logo = new PictureBox();
                logo.SetBounds(24, 12, 68, 68);
                logo.SizeMode = PictureBoxSizeMode.Zoom;
                logo.Image = Image.FromFile(appImagePath);
                Controls.Add(logo);
            }
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Label heading = NewLabel("东方变速器（TH06–TH20）", 105, 18, 405, 30, 15F, true);
            Label intro = NewLabel("输入任意帧率，60 帧为原速。Ctrl+Shift+C 切换窗口。", 107, 54, 403, 25, 9.5F, false);
            Controls.Add(heading); Controls.Add(intro);

            Controls.Add(NewLabel("游戏进程", 24, 97, 85, 28, 10F, false));
            processBox.SetBounds(112, 94, 304, 30);
            processBox.DropDownStyle = ComboBoxStyle.DropDownList;
            Controls.Add(processBox);
            refresh.Text = "刷新"; refresh.SetBounds(426, 93, 84, 32); refresh.Click += delegate { RefreshGames(); };
            Controls.Add(refresh);

            Controls.Add(NewLabel("目标帧率", 24, 143, 85, 28, 10F, false));
            fpsBox.SetBounds(112, 140, 116, 30);
            fpsBox.Minimum = 1; fpsBox.Maximum = 240; fpsBox.DecimalPlaces = 2; fpsBox.Increment = 1; fpsBox.Value = 30;
            Controls.Add(fpsBox);
            AddPreset("60", 242, 140, 60); AddPreset("45", 309, 140, 45); AddPreset("30", 376, 140, 30); AddPreset("15", 443, 140, 15);

            apply.Text = "应用帧率"; apply.SetBounds(24, 190, 240, 46); apply.Font = new Font(Font, FontStyle.Bold); apply.Click += ApplyClicked;
            restore.Text = "恢复 60 帧"; restore.SetBounds(276, 190, 234, 46); restore.Click += RestoreClicked;
            apply.Enabled = false; restore.Enabled = false;
            Controls.Add(apply); Controls.Add(restore);

            GroupBox practiceGroup = new GroupBox();
            practiceGroup.Text = "练习功能（已识别版本）";
            practiceGroup.SetBounds(24, 248, 486, 172);
            practiceSupport.SetBounds(14, 24, 450, 25);
            practiceSupport.Text = "支持状态：选中游戏后读取当前值";
            practiceGroup.Controls.Add(practiceSupport);

            practiceGroup.Controls.Add(practiceLivesLabel=NewLabel("残机", 14, 57, 42, 24, 9F, false));
            ConfigurePracticeNumber(practiceLives, 62, 54, 55, 0, 8, 0, 1);
            practiceGroup.Controls.Add(practiceLives);
            ConfigurePracticeApplyButton(applyPracticeLives, "应用", 125, 53, delegate { ApplyPracticeFields(true, false, false, false); });
            practiceGroup.Controls.Add(applyPracticeLives);
            ConfigureInfiniteCheckBox(infiniteLives, 188, 54);
            practiceGroup.Controls.Add(infiniteLives);

            practiceGroup.Controls.Add(NewLabel("Bomb", 250, 57, 44, 24, 9F, false));
            ConfigurePracticeNumber(practiceBombs, 298, 54, 60, 0, 8, 0, 1);
            practiceGroup.Controls.Add(practiceBombs);
            ConfigurePracticeApplyButton(applyPracticeBombs, "应用", 365, 53, delegate { ApplyPracticeFields(false, true, false, false); });
            practiceGroup.Controls.Add(applyPracticeBombs);
            ConfigureInfiniteCheckBox(infiniteBombs, 423, 54);
            practiceGroup.Controls.Add(infiniteBombs);

            practiceGroup.Controls.Add(NewLabel("B碎片", 14, 94, 42, 24, 9F, false));
            ConfigurePracticeNumber(practiceBombFragments, 62, 91, 55, 0, 4, 0, 1);
            practiceGroup.Controls.Add(practiceBombFragments);
            ConfigurePracticeApplyButton(applyPracticeBombFragments, "应用", 125, 90, delegate { ApplyPracticeFields(false, false, true, false); });
            practiceGroup.Controls.Add(applyPracticeBombFragments);

            practiceGroup.Controls.Add(practicePowerLabel=NewLabel("Power", 250, 94, 44, 24, 9F, false));
            ConfigurePracticeNumber(practicePower, 298, 91, 60, 0, 4, 2, 0.01m);
            practiceGroup.Controls.Add(practicePower);
            ConfigurePracticeApplyButton(applyPracticePower, "应用", 365, 90, delegate { ApplyPracticeFields(false, false, false, true); });
            practiceGroup.Controls.Add(applyPracticePower);
            lockPower.Text = "锁定";
            lockPower.Font = new Font(Font.FontFamily, 9F);
            lockPower.SetBounds(423, 94, 58, 27);
            lockPower.CheckedChanged += PowerLockChanged;
            practiceGroup.Controls.Add(lockPower);
            powerSyncTimer.Interval = 100;
            powerSyncTimer.Tick += PollPowerSync;

            invincibility.Text = "无敌";
            invincibility.SetBounds(14, 130, 130, 27);
            invincibility.CheckedChanged += InvincibilityChanged;
            practiceGroup.Controls.Add(invincibility);

            readPractice.Text = "读取当前值";
            readPractice.SetBounds(260, 129, 100, 30);
            readPractice.Click += ReadPracticeClicked;
            practiceGroup.Controls.Add(readPractice);
            applyPracticeAll.Text = "全部应用";
            applyPracticeAll.SetBounds(370, 129, 100, 30);
            applyPracticeAll.Click += delegate { ApplyPracticeFields(true, true, true, true); };
            practiceGroup.Controls.Add(applyPracticeAll);
            SetPracticeInputsEnabled(false);
            Controls.Add(practiceGroup);
            processBox.SelectedIndexChanged += delegate { ResetPracticePreview(); };
            practiceFreezeTimer.Interval = 100;
            practiceFreezeTimer.Tick += MaintainInfiniteResources;

            status.SetBounds(24, 433, 486, 72);
            status.BorderStyle = BorderStyle.FixedSingle;
            status.Padding = new Padding(8);
            status.AutoSize = false;
            status.UseCompatibleTextRendering = true;
            status.Text = "正在启动变速后端……";
            Controls.Add(status);

            Label version = NewLabel("v0.1.1 · 开发分支 · 非官方个人工具", 24, 526, 240, 24, 9F, false);
            version.ForeColor = Color.DimGray;
            Controls.Add(version);
            LinkLabel biliLink = new LinkLabel();
            biliLink.Text = "B站：斯塔罗尔"; biliLink.SetBounds(272, 526, 112, 24);
            biliLink.LinkClicked += delegate { OpenUrl("https://space.bilibili.com/456339208"); };
            Controls.Add(biliLink);
            LinkLabel githubLink = new LinkLabel();
            githubLink.Text = "GitHub 反馈"; githubLink.SetBounds(400, 526, 110, 24);
            githubLink.LinkClicked += delegate { OpenUrl("https://github.com/StarLorentz/TouhouSpeedController/issues"); };
            Controls.Add(githubLink);

            Activated += delegate { TopMost = true; };
            Deactivate += delegate { TopMost = false; };
            focusHotkeyFallbackTimer.Interval = 30;
            focusHotkeyFallbackTimer.Tick += delegate { PollFocusHotkey(); };
            Shown += delegate
            {
                focusHotkeyRegistered = RegisterHotKey(Handle, FocusHotkeyId,
                    ModControl | ModShift | ModNoRepeat, (uint)Keys.C);
                if (!focusHotkeyRegistered) Text += "（快捷键被占用）";
                focusHotkeyFallbackTimer.Start();
                BeginInitializeBackend();
            };
            FormClosing += delegate
            {
                closing = true;
                focusHotkeyFallbackTimer.Stop();
                if (focusHotkeyRegistered)
                {
                    UnregisterHotKey(Handle, FocusHotkeyId);
                    focusHotkeyRegistered = false;
                }
                practiceFreezeTimer.Stop();
                try { StopPowerSync(); }
                catch (Exception ex)
                {
                    MessageBox.Show("恢复 Power 同步入口失败：" + ex.Message + "\n请退出相应游戏恢复原始代码。",
                        "东方变速器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                try { DisableInvincibility(); }
                catch (Exception ex)
                {
                    MessageBox.Show("恢复无敌补丁失败：" + ex.Message + "\n请退出相应游戏以恢复原始代码。",
                        "东方变速器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                if (backend != null) backend.Dispose();
            };
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam.ToInt32() == FocusHotkeyId)
            {
                if (!focusHotkeyHeld)
                {
                    focusHotkeyHeld = true;
                    ToggleWindowFromHotkey("系统快捷键");
                }
                return;
            }
            base.WndProc(ref message);
        }

        private void PollFocusHotkey()
        {
            bool chordDown = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0 &&
                (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0 &&
                (GetAsyncKeyState((int)Keys.C) & 0x8000) != 0;
            if (!chordDown) { focusHotkeyHeld = false; return; }
            if (focusHotkeyHeld || !IsTouhouGameForeground()) return;
            focusHotkeyHeld = true;
            ToggleWindowFromHotkey("游戏窗口按键检测");
        }

        private bool IsTouhouGameForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            uint foregroundPid;
            GetWindowThreadProcessId(foreground, out foregroundPid);
            if (foregroundPid == 0) return false;
            try
            {
                using (Process foregroundProcess = Process.GetProcessById((int)foregroundPid))
                    return IsTouhouProcessName(foregroundProcess.ProcessName);
            }
            catch { return false; }
        }

        private void ToggleWindowFromHotkey(string source)
        {
            if (WindowState != FormWindowState.Minimized && GetForegroundWindow() == Handle)
            {
                SetStatus(source + "已识别，窗口已最小化。", false);
                TopMost = false;
                WindowState = FormWindowState.Minimized;
                return;
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            TopMost = true;
            SetForegroundWindow(Handle);
            Activate();
            BringToFront();
            if (GetForegroundWindow() != Handle)
                SetStatus(source + "已识别，但系统未将变速器切到前台；可先用 Alt+Tab 切换。", true);
            else
                SetStatus(source + "已识别，变速器已呼出。", false);
        }

        private Label NewLabel(string text, int x, int y, int w, int h, float size, bool bold)
        {
            Label label = new Label(); label.Text = text; label.SetBounds(x, y, w, h);
            label.Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            return label;
        }

        private void AddPreset(string text, int x, int y, decimal value)
        {
            Button button = new Button(); button.Text = text; button.SetBounds(x, y, 60, 31);
            button.Click += delegate { fpsBox.Value = value; };
            Controls.Add(button);
        }

        private void BeginInitializeBackend()
        {
            RefreshGames();
            SetStatus("界面已就绪，正在后台启动变速后端……", false);
            Thread worker = new Thread(delegate()
            {
                SpeedBackend ready = null;
                Exception error = null;
                try { ready = new SpeedBackend(); }
                catch (Exception ex) { error = ex; }

                if (closing)
                {
                    if (ready != null) ready.Dispose();
                    return;
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (closing) { if (ready != null) ready.Dispose(); return; }
                        if (error != null)
                        {
                            SetStatus("启动失败：" + error.Message, true);
                            return;
                        }
                        backend = ready;
                        apply.Enabled = true; restore.Enabled = true;
                        RefreshGames();
                        if (processBox.Items.Count == 0)
                            SetStatus("变速后端已就绪。请启动 TH06–TH20，再点“刷新”。", false);
                    });
                }
                catch { if (ready != null) ready.Dispose(); }
            });
            worker.IsBackground = false;
            worker.Name = "SpeedBackendInitializer";
            worker.Start();
        }

        private void RefreshGames()
        {
            int selectedPid = processBox.SelectedItem is GameProcess ? ((GameProcess)processBox.SelectedItem).Process.Id : -1;
            List<GameProcess> games = Process.GetProcesses()
                .Where(IsTouhouProcess)
                .OrderBy(p => p.ProcessName)
                .Select(CreateGameProcess)
                .Where(g => g != null)
                .ToList();
            processBox.Items.Clear();
            foreach (GameProcess game in games) processBox.Items.Add(game);
            if (games.Count == 0)
            {
                SetStatus("未找到东方游戏。请先启动 TH06–TH20，再点“刷新”。", false);
                return;
            }
            int index = games.FindIndex(g => g.Process.Id == selectedPid);
            processBox.SelectedIndex = index >= 0 ? index : 0;
            SetStatus("已找到 " + games.Count + " 个东方游戏进程。", false);
        }

        private static bool IsTouhouProcess(Process p)
        {
            try
            {
                return IsTouhouProcessName(p.ProcessName);
            }
            catch { return false; }
        }

        internal static bool IsTouhouProcessName(string processName)
        {
            if (String.IsNullOrWhiteSpace(processName)) return false;
            string name = processName.Trim().ToLowerInvariant();

            // TH06–TH20 main games, including common language suffixes and the
            // Steam TH06 Classic/New Classic executables: th06c and th06nc.
            Match main = Regex.Match(name, @"^th(\d{2})(?:[a-z]{1,4}| \([^)]+\))?$");
            if (main.Success)
            {
                int n;
                if (Int32.TryParse(main.Groups[1].Value, out n) && n >= 6 && n <= 20) return true;
            }

            // Common decimal-numbered Touhou games such as th095 and th125.
            return Regex.IsMatch(name, @"^th\d{3}(?:[a-z]{1,4})?$");
        }

        private static GameProcess CreateGameProcess(Process p)
        {
            try
            {
                bool is64 = ProcessArchitecture.Is64Bit(p);
                string title = p.MainWindowTitle;
                string label = p.ProcessName + ".exe  |  PID " + p.Id + "  |  " + (is64 ? "64 位" : "32 位");
                if (!String.IsNullOrWhiteSpace(title)) label += "  |  " + title;
                return new GameProcess { Process = p, Is64Bit = is64, Display = label };
            }
            catch { return null; }
        }

        private void ApplyClicked(object sender, EventArgs e)
        {
            if (backend == null) { SetStatus("变速后端仍在启动，请稍候。", true); return; }
            GameProcess game = processBox.SelectedItem as GameProcess;
            if (game == null) { SetStatus("请先启动游戏并点击“刷新”。", true); return; }
            try
            {
                backend.Apply(game.Process, game.Is64Bit, fpsBox.Value);
                decimal factor = fpsBox.Value / 60m;
                SetStatus(String.Format("已应用：{0}.exe → {1} 帧（{2:0.###} 倍速）", game.Process.ProcessName, fpsBox.Value, factor), false);
            }
            catch (Exception ex) { SetStatus("应用失败：" + ex.Message + "。可尝试右键以管理员身份运行。", true); }
        }

        private void RestoreClicked(object sender, EventArgs e)
        {
            if (backend == null) { SetStatus("变速后端仍在启动，请稍候。", true); return; }
            try { backend.Restore(); fpsBox.Value = 60; SetStatus("已恢复 60 帧（1 倍速）。", false); }
            catch (Exception ex) { SetStatus("恢复失败：" + ex.Message, true); }
        }

        private void ResetPracticePreview()
        {
            StopInfiniteResources();
            try { StopPowerSync(); }
            catch (Exception ex) { SetStatus("停止 Power 同步失败：" + ex.Message + " 请退出相应游戏。", true); }
            try { DisableInvincibility(); }
            catch (Exception ex)
            {
                SetStatus("关闭无敌失败：" + ex.Message, true);
                MessageBox.Show("恢复无敌补丁失败：" + ex.Message + "\n请退出相应游戏以恢复原始代码。",
                    "东方变速器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            practiceSupport.Text = "支持状态：选中游戏后读取当前值";
            practiceSupport.ForeColor = SystemColors.ControlText;
            practiceLives.Value = 0;
            practiceBombs.Value = 0;
            practiceBombFragments.Value = 0;
            practicePower.Value = 0;
            SetPracticeInputsEnabled(false);
            GameProcess selected = processBox.SelectedItem as GameProcess;
            string name = selected == null ? String.Empty : selected.Process.ProcessName.ToLowerInvariant();
            ConfigurePracticeOptions(name);
            bool invincibilityOnly = name == "th06c" || name == "th06nc" || name == "th07";
            readPractice.Enabled = !invincibilityOnly;
            if (invincibilityOnly)
            {
                practiceSupport.Text = "支持状态：此作可测试无敌；资源修改尚未接入";
                invincibility.Enabled = true;
            }
        }

        private void ReadPracticeClicked(object sender, EventArgs e)
        {
            GameProcess game = processBox.SelectedItem as GameProcess;
            if (game == null)
            {
                SetStatus("请先启动游戏并点击“刷新”。", true);
                return;
            }

            try
            {
                string name = game.Process.ProcessName.ToLowerInvariant();
                bool direct = PracticeMemoryAccess.SupportsDirectName(name);
                PracticeSnapshot snapshot = direct
                    ? PracticeMemoryAccess.ReadDirect(game.Process)
                    : PracticeMemoryAccess.ReadTh16(game.Process);
                practiceSupport.Text = "支持状态：已识别 " + snapshot.ProfileId;
                practiceSupport.ForeColor = Color.DarkGreen;
                ConfigurePracticeOptions(name);
                ShowPracticeSnapshot(snapshot);
                bool inGame = direct ? snapshot.InGame : snapshot.Power >= 100 && snapshot.Power <= 400;
                SetPracticeInputsEnabled(inGame);
                string canonical=PracticeMemoryAccess.CanonicalName(name);
                SetStatus(inGame
                    ? (name=="th09" ? "TH09 仅操作 P1；生命 10=5颗心，蓄力 4.00=满槽。此作无 Bomb、B碎片或普通 Power。"
                       : name=="th08" ? "TH08 已读取；Power 范围 0–128，按本作原规则影响射击；此作没有 B碎片。"
                       : canonical=="th10" ? "TH10 残机、Power 已读取；Bomb 消耗 Power。锁定 Power 可维持资源，最小步长 0.05。"
                       : canonical=="th11" ? "TH11 残机、Power 已读取；Bomb 共用 Power。上限和步长按当前机体设置，修改值按原生点数取整。"
                       : "资源已读取；可单项应用或全部应用，Power 同步在游戏运行时完成。")
                    : "版本已识别；请进入实际关卡后重新读取，再应用数值。", false);
            }
            catch (InvalidDataException ex)
            {
                practiceSupport.Text = "支持状态：版本已识别，资源读数未通过校验";
                practiceSupport.ForeColor = Color.DarkRed;
                SetPracticeInputsEnabled(false);
                SetStatus("读取失败：" + ex.Message + " 请进入关卡后重试。", true);
            }
            catch (Exception ex)
            {
                practiceSupport.Text = "支持状态：当前进程或版本不受支持";
                practiceSupport.ForeColor = Color.DarkRed;
                SetPracticeInputsEnabled(false);
                SetStatus("读取失败：" + ex.Message, true);
            }
        }

        private void ApplyPracticeFields(bool changeLives, bool changeBombs, bool changeBombFragments, bool changePower)
        {
            GameProcess game = processBox.SelectedItem as GameProcess;
            if (game == null) { SetStatus("请先选择已适配的游戏进程。", true); return; }

            try
            {
                int? lives = changeLives ? (int?)Decimal.ToInt32(practiceLives.Value) : null;
                int? bombs = changeBombs ? (int?)Decimal.ToInt32(practiceBombs.Value) : null;
                int? fragments = changeBombFragments ? (int?)Decimal.ToInt32(practiceBombFragments.Value) : null;
                int? power = changePower ? (int?)Decimal.ToInt32(practicePower.Value * 100m) : null;
                string name = game.Process.ProcessName.ToLowerInvariant();
                PracticeOptions options=PracticeMemoryAccess.Options(name);
                if(!options.HasBombs) { changeBombs=false; bombs=null; }
                if(!options.HasFragments) { changeBombFragments=false; fragments=null; }
                bool syncPower = PowerSyncProfile.Supports(name) && changePower;
                bool direct = name != "th16";
                if (syncPower) EnsurePowerSync(game.Process);
                PracticeSnapshot snapshot = syncPower && !changeLives && !changeBombs && !changeBombFragments
                    ? (direct ? PracticeMemoryAccess.ReadDirect(game.Process) : PracticeMemoryAccess.ReadTh16(game.Process))
                    : (direct ? PracticeMemoryAccess.WriteDirect(game.Process,lives,bombs,fragments,syncPower ? null : power)
                        : PracticeMemoryAccess.WriteTh16(game.Process,lives,bombs,fragments,syncPower ? null : power));
                if (syncPower)
                {
                    power=powerSync.RequestDisplayed(power.Value, lockPower.Checked);
                    powerRequestPending = true;
                    powerSyncTimer.Start();
                }
                if(changePower && options.MemoryPowerLock && lockPower.Checked) { memoryPowerTarget=power.Value; practiceFreezeTimer.Start(); }
                ShowPracticeSnapshot(snapshot);
                practicePower.Value = syncPower ? power.Value / 100m : snapshot.Power / 100m;
                SetStatus(syncPower ? "Power 请求已提交；回到游戏后由游戏更新线程同步攻击。"
                    : changePower && options.MemoryPowerLock ? options.PowerLabel+" 已应用；锁定勾选后会维持输入值。"
                    : changePower
                    ? "Power 数值已写入；攻击需等游戏重建配置（如拾取 P 跨整档）才同步。"
                    : "资源数值已写入并校验；游戏界面可能稍后才同步。", false);
            }
            catch (Exception ex) { SetStatus("应用失败：" + ex.Message, true); }
        }

        private void EnsurePowerSync(Process game)
        {
            if (powerSync != null && powerSync.ProcessId == game.Id) return;
            if (powerSync != null) StopPowerSync();
            powerSync = PowerSyncSession.Attach(game);
        }

        private void PowerLockChanged(object sender, EventArgs e)
        {
            if (updatingPowerLock) return;
            GameProcess selectedPowerGame=processBox.SelectedItem as GameProcess;
            if(selectedPowerGame!=null && PracticeMemoryAccess.Options(selectedPowerGame.Process.ProcessName).MemoryPowerLock)
            {
                if(!lockPower.Checked) {
                    if(!HasInfiniteSelection()) practiceFreezeTimer.Stop();
                    SetStatus("锁定已关闭，恢复游戏自身的增减规则。",false); return;
                }
                try {
                    memoryPowerTarget=Decimal.ToInt32(practicePower.Value*100m);
                    PracticeMemoryAccess.WriteDirect(selectedPowerGame.Process,null,null,null,memoryPowerTarget);
                    practiceFreezeTimer.Start(); SetStatus("已锁定当前输入值；应用新值可更换锁定目标。",false);
                } catch(Exception ex) {
                    updatingPowerLock=true; lockPower.Checked=false; updatingPowerLock=false;
                    SetStatus("锁定失败："+ex.Message,true);
                }
                return;
            }
            if (!lockPower.Checked)
            {
                try { if (powerSync != null) powerSync.Unlock(); SetStatus("Power 锁定已关闭，恢复游戏自身的增减规则。", false); }
                catch (Exception ex)
                {
                    updatingPowerLock = true; lockPower.Checked = true; updatingPowerLock = false;
                    SetStatus("取消锁定失败：" + ex.Message, true);
                }
                return;
            }
            try
            {
                GameProcess game = processBox.SelectedItem as GameProcess;
                if (game == null || !PowerSyncProfile.Supports(game.Process.ProcessName))
                    throw new NotSupportedException("此版本尚未接入 Power 同步和锁定。");
                EnsurePowerSync(game.Process);
                int accepted=powerSync.RequestDisplayed(Decimal.ToInt32(practicePower.Value * 100m), true);
                practicePower.Value=accepted/100m;
                powerRequestPending = true;
                powerSyncTimer.Start();
                SetStatus("Power 锁定请求已提交；回到游戏后生效。应用新值可更新锁定目标。", false);
            }
            catch (Exception ex)
            {
                updatingPowerLock = true; lockPower.Checked = false; updatingPowerLock = false;
                SetStatus("锁定失败：" + ex.Message, true);
            }
        }

        private void PollPowerSync(object sender, EventArgs e)
        {
            if (powerSync == null) { powerSyncTimer.Stop(); return; }
            try
            {
                PowerSyncStatus state = powerSync.Status();
                if(state.Result==4) {
                    StopPowerSync(); StopInfiniteResources(); SetPracticeInputsEnabled(false);
                    SetStatus("机体的火力上限或单位已变化，资源锁定已停止；请重新读取当前值。",false); return;
                }
                if (powerRequestPending && !state.Pending)
                {
                    powerRequestPending = false;
                    SetStatus(state.Result == 3 ? "游戏场景已改变，Power 请求已取消，请进入关卡后重新应用。"
                        : lockPower.Checked ? "Power 与攻击已同步，锁定已开启。应用新值可更新锁定目标。"
                        : "Power 与攻击已同步；自然增减也会在游戏更新时检查刷新。", state.Result == 3);
                }
            }
            catch (Exception ex)
            {
                powerSyncTimer.Stop();
                try { StopPowerSync(); } catch { }
                SetStatus("Power 同步已停止：" + ex.Message, true);
            }
        }

        private void StopPowerSync()
        {
            powerSyncTimer.Stop();
            if (powerSync != null) { powerSync.Dispose(); powerSync = null; }
            powerRequestPending = false;
            updatingPowerLock = true; lockPower.Checked = false; updatingPowerLock = false;
        }

        private void ConfigureInfiniteCheckBox(CheckBox box, int x, int y)
        {
            box.Text = "无限"; box.Font = new Font(Font.FontFamily, 9F); box.SetBounds(x, y, 58, 27);
            box.CheckedChanged += InfiniteSelectionChanged;
        }

        private void InvincibilityChanged(object sender, EventArgs e)
        {
            if (updatingInfiniteChecks) return;
            if (!invincibility.Checked)
            {
                try
                {
                    string code = activeInvincibility == null ? "游戏" : activeInvincibility.GameCode;
                    DisableInvincibility();
                    SetStatus("无敌已关闭，" + code + " 受击代码已恢复。", false);
                }
                catch (Exception ex)
                {
                    updatingInfiniteChecks = true;
                    invincibility.Checked = true;
                    updatingInfiniteChecks = false;
                    SetStatus("关闭无敌失败：" + ex.Message, true);
                    MessageBox.Show("恢复无敌补丁失败：" + ex.Message + "\n请退出相应游戏以恢复原始代码。",
                        "东方变速器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            GameProcess game = processBox.SelectedItem as GameProcess;
            try
            {
                if (activeInvincibility != null)
                    throw new InvalidOperationException("上一个游戏的无敌补丁尚未恢复。");
                if (game == null) throw new InvalidOperationException("请先选择已识别的东方游戏进程。");
                activeInvincibility = InvincibilityPatch.Enable(game.Process);
                SetStatus(activeInvincibility.GameCode + " 无敌已开启；练习录像可能不同步。", false);
            }
            catch (Exception ex)
            {
                updatingInfiniteChecks = true;
                invincibility.Checked = false;
                updatingInfiniteChecks = false;
                SetStatus("开启无敌失败：" + ex.Message, true);
            }
        }

        private void DisableInvincibility()
        {
            if (activeInvincibility != null)
            {
                activeInvincibility.Dispose();
                activeInvincibility = null;
            }
            updatingInfiniteChecks = true;
            invincibility.Checked = false;
            updatingInfiniteChecks = false;
        }

        private void InfiniteSelectionChanged(object sender, EventArgs e)
        {
            if (updatingInfiniteChecks) return;
            if (!HasInfiniteSelection())
            {
                practiceFreezeTimer.Stop();
                SetStatus("无限资源已停止。当前资源数值保持不变。", false);
                return;
            }

            if (ApplyInfiniteResources())
            {
                practiceFreezeTimer.Start();
                SetStatus("已锁定勾选的无限资源；取消勾选或退出工具即可停止。", false);
            }
        }

        private void MaintainInfiniteResources(object sender, EventArgs e)
        {
            ApplyInfiniteResources();
        }

        private bool ApplyInfiniteResources()
        {
            GameProcess game = processBox.SelectedItem as GameProcess;
            if (game == null)
            {
                StopInfiniteResources();
                SetStatus("无限资源已停止：游戏进程不存在。", true);
                return false;
            }

            try
            {
                int? lives=infiniteLives.Checked ? (int?)Decimal.ToInt32(practiceLives.Value) : null;
                int? bombs=infiniteBombs.Checked ? (int?)Decimal.ToInt32(practiceBombs.Value) : null;
                PracticeOptions options=PracticeMemoryAccess.Options(game.Process.ProcessName);
                int? power=options.MemoryPowerLock && lockPower.Checked ? (int?)memoryPowerTarget : null;
                if(PracticeMemoryAccess.CanonicalName(game.Process.ProcessName)=="th16")
                    PracticeMemoryAccess.WriteTh16(game.Process,lives,bombs,null,null);
                else if(options.InfiniteResources) PracticeMemoryAccess.WriteDirect(game.Process,lives,bombs,null,power);
                else throw new NotSupportedException("此作尚未开放无限资源。");
                return true;
            }
            catch (Exception ex)
            {
                StopInfiniteResources();
                SetStatus("无限资源已停止：" + ex.Message, true);
                return false;
            }
        }

        private bool HasInfiniteSelection()
        {
            GameProcess selected=processBox.SelectedItem as GameProcess;
            return infiniteLives.Checked || infiniteBombs.Checked ||
                (selected!=null && PracticeMemoryAccess.Options(selected.Process.ProcessName).MemoryPowerLock && lockPower.Checked);
        }

        private void StopInfiniteResources()
        {
            practiceFreezeTimer.Stop();
            updatingInfiniteChecks = true;
            try
            {
                infiniteLives.Checked = false;
                infiniteBombs.Checked = false;
                GameProcess selected=processBox.SelectedItem as GameProcess;
                if(selected!=null && PracticeMemoryAccess.Options(selected.Process.ProcessName).MemoryPowerLock) {
                    updatingPowerLock=true; lockPower.Checked=false; updatingPowerLock=false;
                }
            }
            finally { updatingInfiniteChecks = false; }
        }

        private static void ConfigurePracticeApplyButton(Button button, string text, int x, int y, EventHandler handler)
        {
            button.Text = text; button.SetBounds(x, y, 56, 28); button.Click += handler;
        }

        private static void ConfigurePracticeNumber(NumericUpDown box, int x, int y, int width,
            decimal min, decimal max, int decimalPlaces, decimal increment)
        {
            box.SetBounds(x, y, width, 26);
            box.Minimum = min; box.Maximum = max; box.DecimalPlaces = decimalPlaces; box.Increment = increment;
            box.TextAlign = HorizontalAlignment.Center;
        }

        private void SetPracticeInputsEnabled(bool enabled)
        {
            GameProcess selected=processBox.SelectedItem as GameProcess;
            PracticeOptions options=PracticeMemoryAccess.Options(selected==null ? "" : selected.Process.ProcessName);
            SetPracticeInputsEnabled(enabled,options,selected!=null && PowerSyncProfile.Supports(selected.Process.ProcessName));
        }
        private void SetPracticeInputsEnabled(bool enabled,PracticeOptions options,bool powerSupported)
        {
            practiceLives.Enabled = enabled;
            practiceBombs.Enabled = enabled && options.HasBombs;
            practiceBombFragments.Enabled = enabled && options.HasFragments;
            practicePower.Enabled = enabled;
            lockPower.Enabled = enabled && (powerSupported||options.MemoryPowerLock);
            applyPracticeLives.Enabled = enabled;
            applyPracticeBombs.Enabled = enabled && options.HasBombs;
            applyPracticeBombFragments.Enabled = enabled && options.HasFragments;
            applyPracticePower.Enabled = enabled;
            applyPracticeAll.Enabled = enabled;
            infiniteLives.Enabled = enabled && options.InfiniteResources;
            infiniteBombs.Enabled = enabled && options.InfiniteResources && options.HasBombs;
            invincibility.Enabled = enabled || activeInvincibility != null;
        }

        private void ConfigurePracticeOptions(string name)
        {
            PracticeOptions o=PracticeMemoryAccess.Options(name);
            practiceLivesLabel.Text=o.LivesLabel; practicePowerLabel.Text=o.PowerLabel;
            practiceLives.Value=0; practiceLives.Maximum=o.LivesMax;
            practiceBombs.Value=0; practiceBombs.Maximum=o.BombsMax;
            practiceBombFragments.Value=0; practiceBombFragments.Maximum=o.FragmentsMax;
            practicePower.Value=0; practicePower.Maximum=o.PowerMax/100m;
            practicePower.DecimalPlaces=o.IntegerPower ? 0 : 2;
            practicePower.Increment=o.IntegerPower ? 1m : o.PowerStep;
            invincibility.Text=PracticeMemoryAccess.CanonicalName(name)=="th09" ? "无敌 P1" : "无敌";
        }
        private void ShowPracticeSnapshot(PracticeSnapshot snapshot)
        {
            if(snapshot.DisplayPowerMaximum>0) {
                practicePower.Value=0; practicePower.Maximum=snapshot.DisplayPowerMaximum/100m;
                practicePower.Increment=1m/snapshot.NativePowerDivisor;
            }
            practiceLives.Value=Math.Max(0,Math.Min(practiceLives.Maximum,snapshot.Lives));
            practiceBombs.Value=Math.Max(0,Math.Min(practiceBombs.Maximum,snapshot.Bombs));
            practiceBombFragments.Value=Math.Max(0,Math.Min(practiceBombFragments.Maximum,snapshot.BombFragments));
            practicePower.Value=Math.Max(0,Math.Min(practicePower.Maximum,snapshot.Power/100m));
        }

        private void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.ForeColor = error ? Color.DarkRed : Color.DarkGreen;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(url);
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开链接：" + ex.Message, "东方变速器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    internal static class ProcessArchitecture
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process(IntPtr processHandle, out bool wow64Process);
        public static bool Is64Bit(Process process)
        {
            if (!Environment.Is64BitOperatingSystem) return false;
            bool wow64;
            if (!IsWow64Process(process.Handle, out wow64)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return !wow64;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
