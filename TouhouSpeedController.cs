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
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

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
        private readonly ComboBox processBox = new ComboBox();
        private readonly NumericUpDown fpsBox = new NumericUpDown();
        private readonly Label status = new Label();
        private readonly Button apply = new Button();
        private readonly Button restore = new Button();
        private readonly Button refresh = new Button();
        private SpeedBackend backend;
        private volatile bool closing;

        public MainForm()
        {
            Text = "东方变速器";
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(540, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
            if (File.Exists(logoPath))
            {
                PictureBox logo = new PictureBox();
                logo.SetBounds(24, 12, 68, 68);
                logo.SizeMode = PictureBoxSizeMode.Zoom;
                logo.Image = Image.FromFile(logoPath);
                Controls.Add(logo);
            }
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Label heading = NewLabel("东方变速器（TH06–TH20）", 105, 18, 405, 30, 15F, true);
            Label intro = NewLabel("输入任意帧率，60 帧为原速。", 107, 54, 403, 25, 9.5F, false);
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

            status.SetBounds(24, 250, 486, 54);
            status.BorderStyle = BorderStyle.FixedSingle;
            status.Padding = new Padding(8);
            status.Text = "正在启动变速后端……";
            Controls.Add(status);

            Label version = NewLabel("v0.1.0 · 非官方个人工具", 24, 318, 210, 24, 9F, false);
            version.ForeColor = Color.DimGray;
            Controls.Add(version);
            LinkLabel biliLink = new LinkLabel();
            biliLink.Text = "B站：斯塔罗尔"; biliLink.SetBounds(252, 318, 112, 24);
            biliLink.LinkClicked += delegate { OpenUrl("https://space.bilibili.com/456339208"); };
            Controls.Add(biliLink);
            LinkLabel githubLink = new LinkLabel();
            githubLink.Text = "GitHub 反馈"; githubLink.SetBounds(395, 318, 115, 24);
            githubLink.LinkClicked += delegate { OpenUrl("https://github.com/StarLorentz/TouhouSpeedController/issues"); };
            Controls.Add(githubLink);

            Shown += delegate { BeginInitializeBackend(); };
            FormClosing += delegate { closing = true; if (backend != null) backend.Dispose(); };
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
                string name = p.ProcessName.ToLowerInvariant();
                Match main = Regex.Match(name, @"^th(\d{2})(?:[a-z].*)?$");
                if (main.Success)
                {
                    int n;
                    if (Int32.TryParse(main.Groups[1].Value, out n) && n >= 6 && n <= 20) return true;
                }
                return Regex.IsMatch(name, @"^th\d{3}(?:[a-z].*)?$");
            }
            catch { return false; }
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
