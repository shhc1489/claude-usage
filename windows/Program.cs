using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeUsage
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
    }

    // MARK: - Credentials (shared with Claude Code via ~/.claude/.credentials.json)

    class Credentials
    {
        const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        static readonly string[] TokenUrls =
        {
            "https://platform.claude.com/v1/oauth/token",
            "https://console.anthropic.com/v1/oauth/token",
        };
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        readonly string path;
        readonly Dictionary<string, object> root;

        Credentials(string path, Dictionary<string, object> root) { this.path = path; this.root = root; }

        Dictionary<string, object> OAuth =>
            root.TryGetValue("claudeAiOauth", out var o) ? o as Dictionary<string, object> : null;

        public string AccessToken => Get("accessToken") as string;
        string RefreshToken => Get("refreshToken") as string;
        public string Plan => Get("subscriptionType") as string;

        public bool IsExpired
        {
            get
            {
                var v = Get("expiresAt");
                if (v == null) return false;
                var ms = Convert.ToDouble(v);
                return DateTimeOffset.FromUnixTimeMilliseconds((long)ms) < DateTimeOffset.UtcNow.AddMinutes(1);
            }
        }

        object Get(string key) => OAuth != null && OAuth.TryGetValue(key, out var v) ? v : null;

        static string FilePath
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
                return Path.Combine(dir, ".credentials.json");
            }
        }

        public static Credentials Load()
        {
            var p = FilePath;
            if (!File.Exists(p))
                throw new Exception("Claude Code login not found — run `claude` and log in");
            var root = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(p));
            var c = new Credentials(p, root);
            if (c.OAuth == null) throw new Exception("Couldn't read Claude Code credentials");
            return c;
        }

        /// Refreshes the token the same way Claude Code does and writes it back,
        /// so Claude Code keeps working with the rotated refresh token.
        public async Task RefreshAsync(HttpClient http)
        {
            if (RefreshToken == null) throw new Exception("No refresh token — run `claude` and log in");
            var body = Json.Serialize(new Dictionary<string, object>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = RefreshToken,
                ["client_id"] = ClientId,
            });
            Exception last = new Exception("Token refresh failed");
            foreach (var url in TokenUrls)
            {
                try
                {
                    var resp = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                    if (!resp.IsSuccessStatusCode)
                    {
                        last = new Exception("Token refresh rejected — run `claude` and log in");
                        continue;
                    }
                    var tok = Json.Deserialize<Dictionary<string, object>>(await resp.Content.ReadAsStringAsync());
                    var o = OAuth;
                    o["accessToken"] = tok["access_token"];
                    if (tok.TryGetValue("refresh_token", out var rt) && rt != null) o["refreshToken"] = rt;
                    if (tok.TryGetValue("expires_in", out var exp))
                        o["expiresAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(Convert.ToDouble(exp) * 1000);
                    var tmp = path + ".tmp";
                    File.WriteAllText(tmp, Json.Serialize(root));
                    File.Replace(tmp, path, null);
                    return;
                }
                catch (Exception e) { last = e; }
            }
            throw last;
        }
    }

    // MARK: - Usage API

    class UsageWindow
    {
        public double? Utilization;
        public DateTimeOffset? ResetsAt;

        public static UsageWindow From(Dictionary<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out var v) || !(v is Dictionary<string, object> d)) return null;
            var w = new UsageWindow();
            if (d.TryGetValue("utilization", out var u) && u != null) w.Utilization = Convert.ToDouble(u);
            if (d.TryGetValue("resets_at", out var r) && r is string s && DateTimeOffset.TryParse(s, out var dt))
                w.ResetsAt = dt;
            return w.Utilization == null ? null : w;
        }
    }

    class Usage
    {
        public UsageWindow FiveHour, SevenDay, SevenDayOpus, SevenDaySonnet;
    }

    // MARK: - Tray app

    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "ClaudeUsage";

        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Timer timer = new Timer { Interval = 5 * 60 * 1000 };
        readonly UsagePopup popup;
        readonly ToolStripMenuItem startupItem;
        IntPtr iconHandle = IntPtr.Zero;
        bool loading;

        public Usage Usage { get; private set; }
        public string Error { get; private set; }
        public string Plan { get; private set; }
        public DateTime? LastUpdated { get; private set; }

        public TrayApp()
        {
            popup = new UsagePopup(this);

            startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleStartup())
            { Checked = StartsWithWindows };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Refresh", null, async (s, e) => await RefreshAsync());
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, (s, e) => Quit());
            tray.ContextMenuStrip = menu;

            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) popup.Toggle(); };
            tray.Visible = true;
            UpdateIcon();

            timer.Tick += async (s, e) => await RefreshAsync();
            timer.Start();
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (loading) return;
            loading = true;
            popup.SetLoading(true);
            try
            {
                var creds = Credentials.Load();
                Plan = creds.Plan;
                if (creds.IsExpired) await creds.RefreshAsync(http);
                var (status, body) = await FetchAsync(creds.AccessToken);
                if (status == HttpStatusCode.Unauthorized)
                {
                    await creds.RefreshAsync(http);
                    (status, body) = await FetchAsync(creds.AccessToken);
                }
                if (status != HttpStatusCode.OK) throw new Exception($"Usage request failed (HTTP {(int)status})");
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                Usage = new Usage
                {
                    FiveHour = UsageWindow.From(root, "five_hour"),
                    SevenDay = UsageWindow.From(root, "seven_day"),
                    SevenDayOpus = UsageWindow.From(root, "seven_day_opus"),
                    SevenDaySonnet = UsageWindow.From(root, "seven_day_sonnet"),
                };
                LastUpdated = DateTime.Now;
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
            finally
            {
                loading = false;
                UpdateIcon();
                popup.SetLoading(false);
                popup.Render();
            }
        }

        async Task<(HttpStatusCode, string)> FetchAsync(string token)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            var resp = await http.SendAsync(req);
            return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }

        // The tray only shows a small icon, so the percentage is drawn into it.
        void UpdateIcon()
        {
            var pct = Usage?.FiveHour?.Utilization;
            var text = pct == null ? (Error == null ? "…" : "!") : ((int)Math.Round(pct.Value)).ToString();
            var size = SystemInformation.SmallIconSize.Width;

            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var bg = new SolidBrush(Theme.LevelColor(pct, Error != null)))
                using (var path = Theme.RoundedRect(new RectangleF(0, 0, size, size), size / 4f))
                    g.FillPath(bg, path);

                var fontSize = size * (text.Length >= 3 ? 0.42f : 0.58f);
                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, size, size + 1), fmt);

                var old = iconHandle;
                iconHandle = bmp.GetHicon();
                tray.Icon = Icon.FromHandle(iconHandle);
                if (old != IntPtr.Zero) DestroyIcon(old);
            }

            string tip;
            if (Usage?.FiveHour != null)
            {
                tip = $"Claude — session {Math.Round(Usage.FiveHour.Utilization.Value)}%";
                if (Usage.SevenDay != null) tip += $", weekly {Math.Round(Usage.SevenDay.Utilization.Value)}%";
            }
            else tip = Error != null ? "Claude usage — " + Error : "Claude usage — loading";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        static bool StartsWithWindows
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(RunName) != null; }
        }

        void ToggleStartup()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (StartsWithWindows) k.DeleteValue(RunName, false);
                else k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
            }
            startupItem.Checked = StartsWithWindows;
        }

        public void Quit()
        {
            tray.Visible = false;
            tray.Dispose();
            if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
            ExitThread();
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }

    // MARK: - UI

    static class Theme
    {
        public static bool Dark
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
        }

        public static Color Background => Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
        public static Color Text => Dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28);
        public static Color Secondary => Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(100, 100, 100);
        public static Color Track => Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);
        public static Color Border => Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);
        public static readonly Color Accent = Color.FromArgb(217, 119, 87);

        public static Color LevelColor(double? pct, bool error)
        {
            if (error && pct == null) return Color.FromArgb(196, 43, 28);
            if (pct == null) return Color.FromArgb(110, 110, 110);
            if (pct >= 90) return Color.FromArgb(196, 43, 28);
            if (pct >= 70) return Color.FromArgb(214, 120, 0);
            return Accent;
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    class UsagePopup : Form
    {
        readonly TrayApp app;
        readonly float scale;
        DateTime lastHidden;
        bool loading;

        public UsagePopup(TrayApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            KeyPreview = true;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Width = S(300);
            Deactivate += (s, e) => { Hide(); lastHidden = DateTime.Now; };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
            MouseClick += OnClick;
        }

        int S(float v) => (int)Math.Round(v * scale);

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW: keep out of Alt+Tab
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        public void Toggle()
        {
            // Clicking the tray icon while open deactivates (hides) us first; don't reopen.
            if (Visible || (DateTime.Now - lastHidden).TotalMilliseconds < 300) { Hide(); return; }
            Render();
            PositionNearTray();
            Show();
            Activate();
        }

        public void SetLoading(bool value) { loading = value; if (Visible) Invalidate(); }

        public void Render()
        {
            BackColor = Theme.Background;
            Height = S(LayoutContent(null));
            if (Visible) { PositionNearTray(); Invalidate(); }
        }

        void PositionNearTray()
        {
            var cursor = Cursor.Position;
            var area = Screen.FromPoint(cursor).WorkingArea;
            var bounds = Screen.FromPoint(cursor).Bounds;
            int x = Math.Min(Math.Max(cursor.X - Width / 2, area.Left + S(8)), area.Right - Width - S(8));
            int y;
            if (area.Bottom < bounds.Bottom) y = area.Bottom - Height - S(8);      // taskbar at bottom
            else if (area.Top > bounds.Top) y = area.Top + S(8);                    // taskbar at top
            else y = Math.Min(cursor.Y, area.Bottom - Height - S(8));
            if (area.Left > bounds.Left) x = area.Left + S(8);                     // taskbar at left
            else if (area.Right < bounds.Right) x = area.Right - Width - S(8);     // taskbar at right
            Location = new Point(x, y);
        }

        Rectangle refreshRect, quitRect;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            LayoutContent(e.Graphics);
        }

        // Draws the popup when g is set; always returns the content height in unscaled pixels.
        float LayoutContent(Graphics g)
        {
            float pad = 16, w = 300 - pad * 2, y = pad;
            using (var title = new Font("Segoe UI Semibold", S(14), GraphicsUnit.Pixel))
            using (var body = new Font("Segoe UI", S(12), GraphicsUnit.Pixel))
            using (var bold = new Font("Segoe UI Semibold", S(12), GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", S(11), GraphicsUnit.Pixel))
            {
                DrawLabel(g, "Claude usage", title, Theme.Text, pad, y);
                if (app.Plan != null)
                {
                    var plan = char.ToUpper(app.Plan[0]) + app.Plan.Substring(1);
                    float tx = pad + Measure("Claude usage", title) + 8;
                    if (g != null)
                    {
                        var pw = g.MeasureString(plan, small).Width / scale + 8;
                        using (var b = new SolidBrush(Theme.Track))
                        using (var p = Theme.RoundedRect(new RectangleF(S(tx), S(y + 2), S(pw), S(16)), S(8)))
                            g.FillPath(b, p);
                        DrawLabel(g, plan, small, Theme.Text, tx + 4, y + 2.5f);
                    }
                }
                y += 28;

                if (app.Error != null)
                {
                    var h = 16 * Math.Max(1, (int)Math.Ceiling(Measure(app.Error, small) / w));
                    if (g != null)
                        TextRenderer.DrawText(g, app.Error, small,
                            new Rectangle(S(pad), S(y), S(w), S(h)), Color.FromArgb(220, 60, 50),
                            TextFormatFlags.WordBreak);
                    y += h + 10;
                }

                var u = app.Usage;
                if (u != null)
                {
                    y = Row(g, "Current session (5h)", u.FiveHour, pad, w, y, body, bold, small);
                    y = Row(g, "Weekly — all models", u.SevenDay, pad, w, y, body, bold, small);
                    y = Row(g, "Weekly — Opus", u.SevenDayOpus, pad, w, y, body, bold, small);
                    y = Row(g, "Weekly — Sonnet", u.SevenDaySonnet, pad, w, y, body, bold, small);
                }

                if (g != null)
                    using (var pen = new Pen(Theme.Border))
                        g.DrawLine(pen, S(pad), S(y), S(pad + w), S(y));
                y += 10;

                if (app.LastUpdated != null)
                    DrawLabel(g, "Updated " + app.LastUpdated.Value.ToString("t"), small, Theme.Secondary, pad, y + 3);
                var quitW = Measure("Quit", body) + 16;
                var refW = Measure("Refreshing…", body) + 16;
                quitRect = new Rectangle(S(pad + w - quitW), S(y), S(quitW), S(24));
                refreshRect = new Rectangle(S(pad + w - quitW - 6 - refW), S(y), S(refW), S(24));
                if (g != null)
                {
                    DrawButton(g, refreshRect, loading ? "Refreshing…" : "Refresh", body);
                    DrawButton(g, quitRect, "Quit", body);
                }
                y += 24 + pad;
            }
            return y;
        }

        float Row(Graphics g, string label, UsageWindow win, float x, float w, float y,
                  Font body, Font bold, Font small)
        {
            if (win == null) return y;
            var pct = win.Utilization.Value;
            var pctText = Math.Round(pct) + "%";
            DrawLabel(g, label, body, Theme.Text, x, y);
            DrawLabel(g, pctText, bold, Theme.Text, x + w - Measure(pctText, bold), y);
            y += 20;
            if (g != null)
            {
                using (var track = new SolidBrush(Theme.Track))
                using (var tp = Theme.RoundedRect(new RectangleF(S(x), S(y), S(w), S(6)), S(3)))
                    g.FillPath(track, tp);
                var fill = (float)(w * Math.Min(pct, 100) / 100);
                if (fill > 0)
                    using (var bar = new SolidBrush(Theme.LevelColor(pct, false)))
                    using (var bp = Theme.RoundedRect(new RectangleF(S(x), S(y), S(Math.Max(fill, 6)), S(6)), S(3)))
                        g.FillPath(bar, bp);
            }
            y += 12;
            if (win.ResetsAt != null)
            {
                DrawLabel(g, "Resets " + FormatReset(win.ResetsAt.Value.LocalDateTime), small, Theme.Secondary, x, y);
                y += 18;
            }
            return y + 12;
        }

        static string FormatReset(DateTime d)
        {
            var now = DateTime.Now;
            string day = d.Date == now.Date ? "" : d.Date == now.Date.AddDays(1) ? "tomorrow " : d.ToString("ddd d MMM, ");
            var span = d - now;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            var h = (int)span.TotalHours;
            var rel = h >= 24 ? $"{h / 24}d {h % 24}h" : h > 0 ? $"{h}h {span.Minutes}m" : $"{span.Minutes}m";
            return $"{day}{d:h:mm tt} (in {rel})";
        }

        void DrawLabel(Graphics g, string s, Font f, Color c, float x, float y)
        {
            if (g != null) TextRenderer.DrawText(g, s, f, new Point(S(x), S(y)), c, TextFormatFlags.NoPadding);
        }

        float Measure(string s, Font f) =>
            TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPadding).Width / scale;

        void DrawButton(Graphics g, Rectangle r, string label, Font f)
        {
            using (var b = new SolidBrush(Theme.Track))
            using (var p = Theme.RoundedRect(r, S(4)))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, label, f, r, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        async void OnClick(object sender, MouseEventArgs e)
        {
            if (quitRect.Contains(e.Location)) app.Quit();
            else if (refreshRect.Contains(e.Location) && !loading) await app.RefreshAsync();
        }
    }
}
