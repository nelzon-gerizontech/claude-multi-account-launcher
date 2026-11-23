// Claude Multi-Account Launcher v2.2 by Gerizon Technologies - one window, one tab per Claude account.
// Each tab shows claude.ai in Microsoft Edge WebView2 with its own, separate sign-in
// (its own browser data folder), so switching accounts is just clicking a tab.
// Open-source software (MIT License). Copyright (c) 2026 Gerizon Information Technology Solutions.
// Independent tool: not made by, affiliated with, or endorsed by Anthropic. Claude is a trademark of Anthropic, PBC.
// Targets .NET Framework 4.x and the WebView2 Runtime, both built into Windows 10/11.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("Claude Multi-Account Launcher")]
[assembly: AssemblyProduct("Claude Multi-Account Launcher")]
[assembly: AssemblyDescription("Use several Claude accounts in tabs. By Gerizon Technologies.")]
[assembly: AssemblyTrademark("Claude is a trademark of Anthropic, PBC. This product is not affiliated with Anthropic.")]
[assembly: AssemblyCompany("Gerizon Information Technology Solutions")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Gerizon Information Technology Solutions")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

namespace Gerizon.ClaudeMultiAccountLauncher
{
    static class Brand
    {
        public const string Name = "Claude Multi-Account Launcher";
        public const string Company = "Gerizon Technologies";
        public const string LegalName = "Gerizon Information Technology Solutions";
        public const string Disclaimer = "Independent open-source tool. Not made by, affiliated with, or endorsed by Anthropic. Claude is a trademark of Anthropic, PBC.";
    }

    // ------------------------------------------------------------------ data

    [DataContract]
    class Account
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public int ColorIndex;
        [DataMember] public bool SignedIn;
    }

    [DataContract]
    class Store
    {
        [DataMember] public List<Account> Accounts;
        [DataMember] public string ActiveId;
        [DataMember] public List<string> PendingDelete;   // data folders to remove at next start
        [DataMember] public int X, Y, Width, Height;
        [DataMember] public bool Maximized;

        static readonly object Gate = new object();

        public static Store Load()
        {
            lock (Gate)
            {
                Store s = null;
                try
                {
                    if (File.Exists(Paths.StoreFile))
                        using (var f = File.OpenRead(Paths.StoreFile))
                            s = (Store)new DataContractJsonSerializer(typeof(Store)).ReadObject(f);
                }
                catch { s = null; }
                if (s == null) s = new Store();
                if (s.Accounts == null) s.Accounts = new List<Account>();
                if (s.PendingDelete == null) s.PendingDelete = new List<string>();
                return s;
            }
        }

        public void Save()
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Paths.AppData);
                    var tmp = Paths.StoreFile + ".tmp";
                    using (var f = File.Create(tmp))
                        new DataContractJsonSerializer(typeof(Store)).WriteObject(f, this);
                    if (File.Exists(Paths.StoreFile)) File.Delete(Paths.StoreFile);
                    File.Move(tmp, Paths.StoreFile);
                }
                catch { }
            }
        }
    }

    static class Paths
    {
        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string appData;
        public static string AppData { get { return appData ?? (appData = Path.Combine(Local, "Gerizon", "ClaudeMultiAccountLauncher")); } }
        static string LegacyAppData { get { return Path.Combine(Local, "ClaudeAccounts"); } }

        // Brings tabs over from v2.0 ("Claude Accounts"), which kept its data in another folder.
        public static void MigrateFromV2()
        {
            try
            {
                if (Directory.Exists(AppData) || !File.Exists(Path.Combine(LegacyAppData, "tabs.json"))) return;
                Directory.CreateDirectory(Path.GetDirectoryName(AppData));
                Directory.Move(LegacyAppData, AppData);
            }
            catch { appData = LegacyAppData; } // couldn't move it: keep using it where it is
            try
            {
                var oldLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Claude Accounts.lnk");
                if (File.Exists(oldLink)) File.Delete(oldLink);
            }
            catch { }
        }
        public static string StoreFile { get { return Path.Combine(AppData, "tabs.json"); } }
        public static string BrowserData(string id) { return Path.Combine(AppData, "Tabs", id); }
        public static string ThisExe { get { return Application.ExecutablePath; } }
        public static string ThisDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        public static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Brand.Name + ".lnk"); } }
    }

    // ------------------------------------------------------------------ look

    static class Look
    {
        public static readonly Color[] Palette =
        {
            Color.FromArgb(217, 119, 87),  // clay
            Color.FromArgb(59, 130, 246),  // blue
            Color.FromArgb(16, 150, 110),  // green
            Color.FromArgb(139, 92, 246),  // purple
            Color.FromArgb(219, 39, 119),  // pink
            Color.FromArgb(13, 148, 136),  // teal
            Color.FromArgb(202, 138, 4),   // amber
            Color.FromArgb(71, 85, 105),   // slate
        };
        public static readonly Color Bar = Color.FromArgb(238, 235, 228);
        public static readonly Color BarHover = Color.FromArgb(228, 224, 216);
        public static readonly Color Page = Color.FromArgb(250, 249, 245);
        public static readonly Color Border = Color.FromArgb(218, 214, 205);
        public static readonly Color Text = Color.FromArgb(31, 30, 29);
        public static readonly Color Muted = Color.FromArgb(110, 106, 100);
        public static readonly Color Accent = Color.FromArgb(255, 102, 0);   // Gerizon orange #FF6600
        public static readonly Color Navy = Color.FromArgb(26, 26, 46);      // Gerizon navy #1A1A2E
        public static readonly Color NavyHover = Color.FromArgb(48, 48, 78);
        public static readonly Color AccentText = Color.FromArgb(196, 72, 0); // orange dark enough for text

        static readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        public static Image Res(string name)
        {
            Image img;
            if (images.TryGetValue(name, out img)) return img;
            try
            {
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    img = st == null ? null : new Bitmap(Image.FromStream(st));
            }
            catch { img = null; }
            images[name] = img;
            return img;
        }

        public static Color ColorOf(Account a) { return Palette[((a.ColorIndex % Palette.Length) + Palette.Length) % Palette.Length]; }

        public static string Initials(string name)
        {
            var words = (name ?? "").Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0])).ToList();
            if (words.Count == 0) return "?";
            if (words.Count == 1) return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
        }

        public static Bitmap Avatar(Account a, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(ColorOf(a))) g.FillEllipse(b, 0, 0, size - 1, size - 1);
                var text = Initials(a.Name);
                float em = size * (text.Length > 1 ? 0.38f : 0.48f);
                using (var f = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, f, Brushes.White, new RectangleF(0, size * 0.02f, size, size), sf);
            }
            return bmp;
        }

        public static Icon AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Paths.ThisExe); } catch { return SystemIcons.Application; }
        }
    }

    static class Ui
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }

        public static Button Primary(string text)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Look.Navy, ForeColor = Color.White, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Look.NavyHover;
            b.Font = new Font("Segoe UI Semibold", 10f);
            return b;
        }

        public static Button Secondary(string text)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Look.Text, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Look.Border;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 242, 238);
            b.Font = new Font("Segoe UI", 10f);
            return b;
        }

        // Flat icon-style button for the top bar.
        public static Button BarButton(string text, string tip, ToolTip tt)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Look.Bar, ForeColor = Look.Text, Cursor = Cursors.Hand, TabStop = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Look.BarHover;
            b.Font = new Font("Segoe UI", 11f);
            b.Size = new Size(S(38), S(34));
            tt.SetToolTip(b, tip);
            return b;
        }

        public static void Error(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void OpenInDefaultBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }

    // ------------------------------------------------------------------ dialogs

    class BaseDialog : Form
    {
        protected BaseDialog(string title, int width, int height)
        {
            Text = title;
            Font = new Font("Segoe UI", 10f);
            BackColor = Look.Page;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(width), Ui.S(height));
            Icon = Look.AppIcon();
        }

        protected Label AddLabel(string text, int x, int y, int w, int h, float size, FontStyle style, Color color)
        {
            var l = new Label { Text = text, Location = new Point(Ui.S(x), Ui.S(y)), Size = new Size(Ui.S(w), Ui.S(h)), ForeColor = color, Font = new Font("Segoe UI", size, style) };
            Controls.Add(l);
            return l;
        }

        protected Button AddButton(Button b, int x, int y, int w)
        {
            b.Size = new Size(Ui.S(w), Ui.S(38));
            b.Location = new Point(Ui.S(x), Ui.S(y));
            Controls.Add(b);
            return b;
        }
    }

    class NameDialog : BaseDialog
    {
        public TextBox NameBox;
        readonly Func<string, string> validate;

        public NameDialog(string title, string heading, string okText, string initial, Func<string, string> validate)
            : base(title, 440, 200)
        {
            this.validate = validate;
            AddLabel(heading, 20, 18, 400, 26, 12f, FontStyle.Bold, Look.Text);
            AddLabel("A name you'll recognise on the tab, like \"Work\", \"Personal\" or \"Client A\".", 20, 48, 400, 40, 9.5f, FontStyle.Regular, Look.Muted);
            NameBox = new TextBox { Location = new Point(Ui.S(20), Ui.S(86)), Width = Ui.S(400), Text = initial ?? "", MaxLength = 30, Font = new Font("Segoe UI", 11f) };
            Controls.Add(NameBox);
            var ok = AddButton(Ui.Primary(okText), 270, 140, 150);
            ok.Click += delegate { OnOk(); };
            var cancel = AddButton(Ui.Secondary("Cancel"), 160, 140, 100);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok; CancelButton = cancel;
        }

        void OnOk()
        {
            var name = NameBox.Text.Trim();
            var problem = validate(name);
            if (problem != null) { Ui.Error(this, problem); NameBox.Focus(); return; }
            NameBox.Text = name;
            DialogResult = DialogResult.OK;
        }
    }

    class DeleteDialog : BaseDialog
    {
        public DeleteDialog(Account a) : base("Delete account", 440, 220)
        {
            AddLabel("Delete \"" + a.Name + "\" from this app?", 20, 18, 400, 28, 12f, FontStyle.Bold, Look.Text);
            AddLabel("This removes the tab, signs the account out on this PC and deletes its saved data here.\n\n" +
                     "It doesn't delete the Claude account itself. Its chats stay online, and you can add it again any time.",
                     20, 52, 400, 96, 9.5f, FontStyle.Regular, Look.Muted);
            var ok = AddButton(Ui.Primary("Delete"), 290, 160, 130);
            ok.BackColor = Color.FromArgb(185, 28, 28);
            ok.FlatAppearance.MouseOverBackColor = Color.FromArgb(153, 27, 27);
            ok.DialogResult = DialogResult.OK;
            var cancel = AddButton(Ui.Secondary("Cancel"), 180, 160, 100);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
        }
    }

    class AboutDialog : BaseDialog
    {
        public AboutDialog() : base("About " + Brand.Name, 460, 330)
        {
            var logo = Look.Res("gerizon-logo-64.png");
            if (logo != null)
            {
                var pb = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(Ui.S(20), Ui.S(20)), Size = new Size(Ui.S(219), Ui.S(40)), BackColor = Color.Transparent };
                Controls.Add(pb);
            }
            AddLabel(Brand.Name, 20, 78, 420, 28, 13f, FontStyle.Bold, Look.Text);
            AddLabel("Version " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3), 20, 106, 420, 22, 9.5f, FontStyle.Regular, Look.Muted);
            AddLabel("A product of " + Brand.Company + ".\nCopyright (c) 2026 " + Brand.LegalName + ".\nOpen-source software under the MIT License.",
                     20, 136, 420, 62, 9.5f, FontStyle.Regular, Look.Text);
            AddLabel(Brand.Disclaimer + "\nUses Microsoft Edge WebView2.", 20, 204, 420, 60, 8.5f, FontStyle.Regular, Look.Muted);
            var ok = AddButton(Ui.Primary("OK"), 330, 272, 110);
            ok.DialogResult = DialogResult.OK;
            AcceptButton = ok; CancelButton = ok;
        }
    }

    // A small window for sign-in pop-ups (for example single sign-on). It shares the
    // tab's browser data, so the sign-in lands in the right account.
    class PopupForm : Form
    {
        public readonly WebView2 View = new WebView2 { Dock = DockStyle.Fill };

        public PopupForm(string title)
        {
            Text = title;
            Icon = Look.AppIcon();
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(520), Ui.S(680));
            Controls.Add(View);
        }

        public async Task Init(CoreWebView2Environment env)
        {
            await View.EnsureCoreWebView2Async(env);
            View.CoreWebView2.WindowCloseRequested += delegate { Close(); };
            // A link inside the pop-up that wants yet another window opens in this pop-up instead.
            View.CoreWebView2.NewWindowRequested += (s, e) => { e.Handled = true; View.CoreWebView2.Navigate(e.Uri); };
            View.CoreWebView2.DocumentTitleChanged += delegate { if (!string.IsNullOrEmpty(View.CoreWebView2.DocumentTitle)) Text = View.CoreWebView2.DocumentTitle; };
        }
    }

    // ------------------------------------------------------------------ one account's page

    class AccountPage : Panel
    {
        public readonly Account Account;
        readonly MainForm owner;
        WebView2 view;
        CoreWebView2Environment env;
        readonly Panel banner, away;
        readonly Label awayMsg;
        readonly Label loading;
        bool started;

        public event EventHandler SignedInChanged;

        public AccountPage(Account a, MainForm owner)
        {
            Account = a;
            this.owner = owner;
            Dock = DockStyle.Fill;
            BackColor = Look.Page;
            Visible = false;

            loading = new Label { Text = "Opening Claude for " + a.Name + "...", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Look.Muted, Font = new Font("Segoe UI", 11f) };
            Controls.Add(loading);

            banner = new Panel { Dock = DockStyle.Top, Height = Ui.S(46), BackColor = Color.FromArgb(255, 240, 228), Visible = !a.SignedIn };
            var msg = new Label
            {
                Text = "Sign in with " + a.Name + "'s email. If Claude emails you a sign-in link, copy the link, then click \"Paste sign-in link\".",
                AutoSize = false, Location = new Point(Ui.S(14), 0), Height = Ui.S(46), Width = Ui.S(700),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Look.Text, Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            var paste = Ui.Primary("Paste sign-in link");
            paste.Font = new Font("Segoe UI Semibold", 9f);
            paste.Size = new Size(Ui.S(150), Ui.S(30));
            paste.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            paste.Click += delegate { PasteSignInLink(); };
            banner.Controls.Add(paste);
            banner.Controls.Add(msg);
            banner.Resize += delegate
            {
                paste.Location = new Point(banner.Width - paste.Width - Ui.S(12), (banner.Height - paste.Height) / 2);
                msg.Width = Math.Max(Ui.S(100), paste.Left - Ui.S(24));
            };
            Controls.Add(banner);

            // Shown when this tab has left Claude (for example a connector or sign-in page
            // from another company that ended on an error), with a way back.
            away = new Panel { Dock = DockStyle.Top, Height = Ui.S(46), BackColor = Color.FromArgb(232, 238, 250), Visible = false };
            awayMsg = new Label
            {
                AutoSize = false, Location = new Point(Ui.S(14), 0), Height = Ui.S(46), Width = Ui.S(600),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Look.Text, Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            var home = Ui.Primary("Back to Claude");
            home.Font = new Font("Segoe UI Semibold", 9f);
            home.Size = new Size(Ui.S(130), Ui.S(30));
            home.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            home.Click += delegate { GoHome(); };
            var back = new Button { Text = "Go back", FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Look.Text, Cursor = Cursors.Hand, Size = new Size(Ui.S(90), Ui.S(30)), Font = new Font("Segoe UI", 9f), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            back.FlatAppearance.BorderColor = Look.Border;
            back.Click += delegate { GoBack(); };
            away.Controls.Add(home);
            away.Controls.Add(back);
            away.Controls.Add(awayMsg);
            away.Resize += delegate
            {
                home.Location = new Point(away.Width - home.Width - Ui.S(12), (away.Height - home.Height) / 2);
                back.Location = new Point(home.Left - back.Width - Ui.S(8), (away.Height - back.Height) / 2);
                awayMsg.Width = Math.Max(Ui.S(100), back.Left - Ui.S(24));
            };
            Controls.Add(away);
        }

        void UpdateAway()
        {
            if (view == null || view.CoreWebView2 == null) return;
            Uri u;
            bool off = Uri.TryCreate(view.CoreWebView2.Source, UriKind.Absolute, out u)
                && (u.Scheme == "http" || u.Scheme == "https") && !IsClaudeHost(u.Host);
            if (off) awayMsg.Text = "This tab is showing a page from " + u.Host + ", not Claude. If it's stuck or showing an error, go back to Claude.";
            away.Visible = off;
            if (off) banner.Visible = false;
        }

        // WebView2 is created the first time the tab is shown, so unused tabs cost nothing.
        public async void EnsureStarted()
        {
            if (started) return;
            started = true;
            try
            {
                var folder = Paths.BrowserData(Account.Id);
                Directory.CreateDirectory(folder);
                env = await CoreWebView2Environment.CreateAsync(null, folder, null);
                view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Look.Page };
                Controls.Add(view);
                view.BringToFront();
                await view.EnsureCoreWebView2Async(env);
                var core = view.CoreWebView2;
                var st = core.Settings;
                st.AreDevToolsEnabled = false;
                st.IsPasswordAutosaveEnabled = false;
                st.IsGeneralAutofillEnabled = false;
                st.IsStatusBarEnabled = false;
                core.NewWindowRequested += OnNewWindow;
                // Keystrokes inside the Claude page go to the page, so pass the app's own
                // shortcuts (Ctrl+1..9, Ctrl+Tab, Ctrl+T) back to the window. Nothing else is read.
                await core.AddScriptToExecuteOnDocumentCreatedAsync(ShortcutScript);
                core.WebMessageReceived += (s, e) =>
                {
                    Uri src;
                    if (!Uri.TryCreate(e.Source, UriKind.Absolute, out src) || !IsClaudeHost(src.Host)) return;
                    string m = null;
                    try { m = e.TryGetWebMessageAsString(); } catch { }
                    if (m != null) owner.HandleShortcut(m);
                };
                core.SourceChanged += delegate { CheckSignedIn(); UpdateAway(); };
                core.HistoryChanged += delegate { owner.UpdateNav(this); };
                core.NavigationCompleted += (s, e) =>
                {
                    Controls.Remove(loading);
                    CheckSignedIn();
                    if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        owner.SetStatus("Couldn't load Claude for " + Account.Name + ". Check your internet connection, then click Reload.");
                };
                core.Navigate("https://claude.ai/");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                started = false;
                loading.Text = "This PC needs Microsoft Edge WebView2 Runtime to show Claude.";
                if (MessageBox.Show(owner, "This PC needs the free Microsoft Edge WebView2 Runtime (it's part of Windows 11 and most Windows 10 PCs).\n\nOpen Microsoft's download page now?",
                    Brand.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Ui.OpenInDefaultBrowser("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            }
            catch (Exception ex)
            {
                started = false;
                loading.Text = "Claude couldn't open in this tab.\n\n" + ex.Message;
            }
        }

        const string ShortcutScript =
            "(function(){if(window.top!==window||!window.chrome||!chrome.webview)return;" +
            "window.addEventListener('keydown',function(e){if(!e.ctrlKey||e.altKey||e.metaKey)return;var k=e.key,m=null;" +
            "if(k>='1'&&k<='9')m='tab:'+k;else if(k==='Tab')m=e.shiftKey?'prev':'next';else if((k==='t'||k==='T')&&!e.shiftKey)m='add';" +
            "if(m){e.preventDefault();e.stopPropagation();chrome.webview.postMessage(m);}},true);})();";

        static bool IsClaudeHost(string host)
        {
            host = (host ?? "").ToLowerInvariant();
            return host == "claude.ai" || host.EndsWith(".claude.ai") || host == "claude.com" || host.EndsWith(".claude.com")
                || host == "anthropic.com" || host.EndsWith(".anthropic.com");
        }

        static bool IsSignInHost(string host)
        {
            host = (host ?? "").ToLowerInvariant();
            string[] known = { "accounts.google.com", "login.microsoftonline.com", "login.live.com", "appleid.apple.com", "okta.com", "auth0.com", "onelogin.com", "duosecurity.com" };
            return known.Any(k => host == k || host.EndsWith("." + k));
        }

        // Pop-ups for sign-in stay inside this account; ordinary links open in your normal browser.
        async void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            Uri uri;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri)) { e.Handled = true; return; }
            // Sign-in pages and Claude's own pop-ups (such as connecting a connector) open in a
            // separate window that shares this account, so the tab itself stays on Claude and
            // the pop-up can simply be closed if something goes wrong.
            if (IsSignInHost(uri.Host) || IsClaudeHost(uri.Host))
            {
                var deferral = e.GetDeferral();
                try
                {
                    var pop = new PopupForm(Account.Name + " - " + Brand.Name);
                    pop.Show(owner);
                    await pop.Init(env);
                    e.NewWindow = pop.View.CoreWebView2;
                    e.Handled = true;
                }
                catch { e.Handled = true; }
                finally { deferral.Complete(); }
                return;
            }
            e.Handled = true;
            if (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "mailto") Ui.OpenInDefaultBrowser(uri.AbsoluteUri);
        }

        void CheckSignedIn()
        {
            if (view == null || view.CoreWebView2 == null) return;
            Uri u;
            if (!Uri.TryCreate(view.CoreWebView2.Source, UriKind.Absolute, out u) || !IsClaudeHost(u.Host)) return;
            var p = u.AbsolutePath.ToLowerInvariant();
            bool onLogin = p.StartsWith("/login") || p.StartsWith("/magic-link") || p.StartsWith("/logout") || p.StartsWith("/signup") || p.StartsWith("/onboarding");
            bool inApp = p.StartsWith("/new") || p.StartsWith("/chat") || p.StartsWith("/project") || p.StartsWith("/recents") || p.StartsWith("/settings");
            bool now = Account.SignedIn;
            if (onLogin) now = false; else if (inApp) now = true;
            banner.Visible = !now;
            if (now != Account.SignedIn)
            {
                Account.SignedIn = now;
                if (SignedInChanged != null) SignedInChanged(this, EventArgs.Empty);
            }
        }

        public void PasteSignInLink()
        {
            string text = "";
            try { text = (Clipboard.GetText() ?? "").Trim(); } catch { }
            Uri u;
            if (!Uri.TryCreate(text, UriKind.Absolute, out u) || u.Scheme != "https" || !IsClaudeHost(u.Host))
            {
                Ui.Error(owner, "Copy the sign-in link from Claude's email first (right-click the link in the email, then Copy link), then click \"Paste sign-in link\" again.\n\nOnly claude.ai links are accepted.");
                return;
            }
            if (view == null || view.CoreWebView2 == null) { EnsureStarted(); return; }
            view.CoreWebView2.Navigate(u.AbsoluteUri);
        }

        public void Reload()
        {
            if (view != null && view.CoreWebView2 != null) view.CoreWebView2.Reload();
            else { started = false; EnsureStarted(); }
        }

        public bool CanGoBack { get { return view != null && view.CoreWebView2 != null && view.CoreWebView2.CanGoBack; } }

        public void GoBack()
        {
            if (CanGoBack) view.CoreWebView2.GoBack(); else GoHome();
        }

        public void GoHome()
        {
            if (view != null && view.CoreWebView2 != null) view.CoreWebView2.Navigate("https://claude.ai/new");
        }

        public void FocusView() { if (view != null) view.Focus(); }

        // Closes this tab's browser so its data folder can be deleted.
        public void Shutdown()
        {
            try { if (view != null) { Controls.Remove(view); view.Dispose(); view = null; } } catch { }
        }
    }

    // ------------------------------------------------------------------ tab strip

    class TabButton : Control
    {
        public readonly Account Account;
        public bool Active;
        bool hover;

        public TabButton(Account a)
        {
            Account = a;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(38);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.75f);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new Rectangle(0, 0, Width, Height);
            var bg = Active ? Look.Page : (hover ? Look.BarHover : Look.Bar);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
            if (Active)
            {
                using (var b = new SolidBrush(Look.ColorOf(Account))) g.FillRectangle(b, 0, 0, Width, Ui.S(3));
                using (var p = new Pen(Look.Border)) { g.DrawLine(p, 0, 0, 0, Height); g.DrawLine(p, Width - 1, 0, Width - 1, Height); }
            }
            int av = Ui.S(22);
            using (var img = Look.Avatar(Account, av)) g.DrawImage(img, Ui.S(10), (Height - av) / 2 + Ui.S(1));
            var textRect = new Rectangle(Ui.S(10) + av + Ui.S(8), 0, Width - av - Ui.S(26), Height);
            using (var f = new Font("Segoe UI", 9.75f, Active ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Account.Name, f, textRect, Active ? Look.Text : Look.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!Account.SignedIn)
                using (var b = new SolidBrush(Look.Accent)) g.FillEllipse(b, Width - Ui.S(14), Ui.S(8), Ui.S(7), Ui.S(7));
        }
    }

    // ------------------------------------------------------------------ main window

    class MainForm : Form
    {
        readonly Store store;
        readonly Panel bar, tabArea, content, empty;
        readonly Button addBtn, allBtn, backBtn, homeBtn, reloadBtn, menuBtn;
        readonly Label status;
        readonly ToolTip tips = new ToolTip();
        readonly List<TabButton> tabs = new List<TabButton>();
        readonly Dictionary<string, AccountPage> pages = new Dictionary<string, AccountPage>();
        readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        Account active;

        public MainForm()
        {
            store = Store.Load();
            Text = Brand.Name;
            Icon = Look.AppIcon();
            Font = new Font("Segoe UI", 10f);
            BackColor = Look.Bar;
            KeyPreview = true;
            MinimumSize = new Size(Ui.S(640), Ui.S(480));
            StartPosition = FormStartPosition.Manual;
            RestoreBounds2();

            content = new Panel { Dock = DockStyle.Fill, BackColor = Look.Page };
            Controls.Add(content);

            status = new Label { Dock = DockStyle.Bottom, Height = Ui.S(28), BackColor = Color.FromArgb(255, 240, 228), ForeColor = Look.Text, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.S(12), 0, 0, 0), Visible = false, Font = new Font("Segoe UI", 9f) };
            Controls.Add(status);
            statusTimer.Tick += delegate { statusTimer.Stop(); status.Visible = false; };

            bar = new Panel { Dock = DockStyle.Top, Height = Ui.S(46), BackColor = Look.Bar };
            Controls.Add(bar);
            bar.Paint += (s, e) => { using (var p = new Pen(Look.Border)) e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1); };

            var mark = new PictureBox { Image = Look.Res("gerizon-mark.png"), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(Ui.S(10), Ui.S(9)), Size = new Size(Ui.S(28), Ui.S(28)), Cursor = Cursors.Hand, BackColor = Look.Bar };
            tips.SetToolTip(mark, Brand.Name + " by " + Brand.Company);
            mark.Click += delegate { ShowAbout(); };
            bar.Controls.Add(mark);

            tabArea = new Panel { BackColor = Look.Bar, Location = new Point(Ui.S(46), Ui.S(8)), Height = Ui.S(38) };
            bar.Controls.Add(tabArea);

            addBtn = Ui.BarButton("+", "Add an account (Ctrl+T)", tips);
            addBtn.Font = new Font("Segoe UI", 14f);
            addBtn.Click += delegate { AddAccount(); };
            tabArea.Controls.Add(addBtn);

            allBtn = Ui.BarButton("☰", "All accounts", tips);
            allBtn.Click += delegate { ShowAllMenu(); };
            backBtn = Ui.BarButton("←", "Go back (Alt+Left)", tips);
            backBtn.Click += delegate { if (CurrentPage() != null) CurrentPage().GoBack(); };
            homeBtn = Ui.BarButton("⌂", "Back to Claude home (Alt+Home)", tips);
            homeBtn.Click += delegate { if (CurrentPage() != null) CurrentPage().GoHome(); };
            reloadBtn = Ui.BarButton("⟳", "Reload this tab (F5)", tips);
            reloadBtn.Click += delegate { if (CurrentPage() != null) CurrentPage().Reload(); };
            menuBtn = Ui.BarButton("⋯", "More options", tips);
            menuBtn.Click += delegate { ShowAccountMenu(active, menuBtn, true); };
            bar.Controls.Add(allBtn); bar.Controls.Add(backBtn); bar.Controls.Add(homeBtn); bar.Controls.Add(reloadBtn); bar.Controls.Add(menuBtn);
            bar.Resize += delegate { LayoutBar(); };

            empty = BuildEmptyState();
            content.Controls.Add(empty);

            foreach (var a in store.Accounts) AddTabFor(a);
            var start = store.Accounts.FirstOrDefault(a => a.Id == store.ActiveId) ?? store.Accounts.FirstOrDefault();
            if (start != null) Select(start); else ShowEmpty();
            LayoutBar();
        }

        Panel BuildEmptyState()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Look.Page };
            var title = new Label { Text = "Add your first Claude account", Font = new Font("Segoe UI Semibold", 16f), ForeColor = Look.Text, AutoSize = true };
            var sub = new Label { Text = "Each account gets its own tab with its own sign-in. Add as many as you need,\nthen switch between them by clicking the tabs.", Font = new Font("Segoe UI", 10.5f), ForeColor = Look.Muted, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
            var btn = Ui.Primary("+   Add an account");
            btn.Font = new Font("Segoe UI Semibold", 11f);
            btn.Size = new Size(Ui.S(220), Ui.S(46));
            btn.Click += delegate { AddAccount(); };
            var by = new Label { Text = "A product of", Font = new Font("Segoe UI", 9f), ForeColor = Look.Muted, AutoSize = true };
            var logo = new PictureBox { Image = Look.Res("gerizon-logo-64.png"), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Ui.S(175), Ui.S(32)), BackColor = Look.Page };
            p.Controls.Add(title); p.Controls.Add(sub); p.Controls.Add(btn); p.Controls.Add(by); p.Controls.Add(logo);
            p.Resize += delegate
            {
                int cy = p.Height / 2 - Ui.S(90);
                title.Location = new Point((p.Width - title.Width) / 2, cy);
                sub.Location = new Point((p.Width - sub.Width) / 2, cy + Ui.S(46));
                btn.Location = new Point((p.Width - btn.Width) / 2, cy + Ui.S(110));
                logo.Location = new Point((p.Width - logo.Width) / 2, p.Height - logo.Height - Ui.S(36));
                by.Location = new Point((p.Width - by.Width) / 2, logo.Top - by.Height - Ui.S(4));
            };
            return p;
        }

        // ---- tabs

        void AddTabFor(Account a)
        {
            var t = new TabButton(a);
            t.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Select(a);
                else if (e.Button == MouseButtons.Right) ShowAccountMenu(a, t, false);
            };
            tips.SetToolTip(t, a.Name + (a.SignedIn ? "" : " (not signed in yet)"));
            tabs.Add(t);
            tabArea.Controls.Add(t);

            var page = new AccountPage(a, this);
            page.SignedInChanged += delegate { store.Save(); t.Invalidate(); tips.SetToolTip(t, a.Name + (a.SignedIn ? "" : " (not signed in yet)")); };
            pages[a.Id] = page;
            content.Controls.Add(page);
        }

        void LayoutBar()
        {
            int right = bar.Width - Ui.S(6);
            menuBtn.Location = new Point(right - menuBtn.Width, Ui.S(6)); right = menuBtn.Left;
            reloadBtn.Location = new Point(right - reloadBtn.Width, Ui.S(6)); right = reloadBtn.Left;
            homeBtn.Location = new Point(right - homeBtn.Width, Ui.S(6)); right = homeBtn.Left;
            backBtn.Location = new Point(right - backBtn.Width, Ui.S(6)); right = backBtn.Left;
            allBtn.Location = new Point(right - allBtn.Width, Ui.S(6)); right = allBtn.Left - Ui.S(8);
            tabArea.Width = Math.Max(Ui.S(100), right - tabArea.Left);

            int avail = tabArea.Width - addBtn.Width - Ui.S(4);
            int n = tabs.Count;
            int w = n == 0 ? 0 : Math.Max(Ui.S(110), Math.Min(Ui.S(200), avail / n));
            int x = 0;
            foreach (var t in tabs)
            {
                t.Active = active != null && t.Account.Id == active.Id;
                t.SetBounds(x, 0, w, tabArea.Height);
                t.Visible = x + w <= avail || t.Active;
                if (x + w > avail && t.Active) t.Left = Math.Max(0, avail - w); // keep the active tab in view
                t.Invalidate();
                x += w + Ui.S(2);
            }
            int addX = Math.Min(x, Math.Max(0, avail));
            addBtn.Location = new Point(addX + Ui.S(2), (tabArea.Height - addBtn.Height) / 2 - Ui.S(2));
            bool hidden = tabs.Any(t => !t.Visible);
            tips.SetToolTip(allBtn, hidden ? "All accounts (some tabs are hidden - window is narrow)" : "All accounts");
            reloadBtn.Enabled = menuBtn.Enabled = homeBtn.Enabled = active != null;
            backBtn.Enabled = CurrentPage() != null && CurrentPage().CanGoBack;
        }

        public void Select(Account a)
        {
            if (a == null) { ShowEmpty(); return; }
            active = a;
            store.ActiveId = a.Id;
            store.Save();
            empty.Visible = false;
            foreach (var kv in pages) kv.Value.Visible = kv.Key == a.Id;
            var page = pages[a.Id];
            page.BringToFront();
            page.EnsureStarted();
            page.FocusView();
            Text = a.Name + " - " + Brand.Name;
            LayoutBar();
        }

        void ShowEmpty()
        {
            active = null;
            foreach (var p in pages.Values) p.Visible = false;
            empty.Visible = true;
            empty.BringToFront();
            Text = Brand.Name;
            LayoutBar();
        }

        AccountPage CurrentPage() { return active == null ? null : pages[active.Id]; }

        public void SetStatus(string text)
        {
            status.Text = text;
            status.Visible = true;
            statusTimer.Stop(); statusTimer.Start();
        }

        // ---- menus

        void ShowAllMenu()
        {
            var m = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };
            for (int i = 0; i < store.Accounts.Count; i++)
            {
                var a = store.Accounts[i];
                var item = new ToolStripMenuItem(a.Name + (a.SignedIn ? "" : "  (not signed in)"), Look.Avatar(a, Ui.S(16))) { Checked = active == a };
                if (i < 9) item.ShortcutKeyDisplayString = "Ctrl+" + (i + 1);
                item.Click += delegate { Select(a); };
                m.Items.Add(item);
            }
            if (store.Accounts.Count > 0) m.Items.Add(new ToolStripSeparator());
            var add = new ToolStripMenuItem("Add an account...") { ShortcutKeyDisplayString = "Ctrl+T" };
            add.Click += delegate { AddAccount(); };
            var help = new ToolStripMenuItem("Help");
            help.Click += delegate { ShowHelp(); };
            var about = new ToolStripMenuItem("About");
            about.Click += delegate { ShowAbout(); };
            m.Items.Add(add); m.Items.Add(help); m.Items.Add(about);
            m.Closed += delegate { BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            m.Show(allBtn, new Point(0, allBtn.Height));
        }

        void ShowAccountMenu(Account a, Control anchor, bool below)
        {
            if (a == null) return;
            var m = new ContextMenuStrip { Font = new Font("Segoe UI", 9.5f) };
            var page = pages[a.Id];
            var paste = new ToolStripMenuItem("Paste sign-in link");
            paste.Click += delegate { Select(a); page.PasteSignInLink(); };
            var back = new ToolStripMenuItem("Go back") { ShortcutKeyDisplayString = "Alt+Left" };
            back.Click += delegate { Select(a); page.GoBack(); };
            var home = new ToolStripMenuItem("Back to Claude home") { ShortcutKeyDisplayString = "Alt+Home" };
            home.Click += delegate { Select(a); page.GoHome(); };
            var reload = new ToolStripMenuItem("Reload") { ShortcutKeyDisplayString = "F5" };
            reload.Click += delegate { Select(a); page.Reload(); };
            var rename = new ToolStripMenuItem("Rename...");
            rename.Click += delegate { Rename(a); };
            var color = new ToolStripMenuItem("Change colour");
            color.Click += delegate { a.ColorIndex = (a.ColorIndex + 1) % Look.Palette.Length; store.Save(); LayoutBar(); };
            var left = new ToolStripMenuItem("Move left") { Enabled = store.Accounts.IndexOf(a) > 0 };
            left.Click += delegate { MoveTab(a, -1); };
            var right = new ToolStripMenuItem("Move right") { Enabled = store.Accounts.IndexOf(a) < store.Accounts.Count - 1 };
            right.Click += delegate { MoveTab(a, 1); };
            var remove = new ToolStripMenuItem("Delete account...") { ForeColor = Color.FromArgb(185, 28, 28) };
            remove.Click += delegate { Remove(a); };
            m.Items.AddRange(new ToolStripItem[] { back, home, reload, paste, new ToolStripSeparator(), rename, color, left, right, new ToolStripSeparator(), remove });
            m.Closed += delegate { BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            if (below) m.Show(anchor, new Point(anchor.Width, anchor.Height), ToolStripDropDownDirection.BelowLeft);
            else m.Show(anchor, new Point(0, anchor.Height));
        }

        // ---- account actions

        void AddAccount()
        {
            using (var d = new NameDialog("Add an account", "Add a Claude account", "Add", "", n => ValidateName(n, null)))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var a = new Account { Id = NewId(), Name = d.NameBox.Text, ColorIndex = NextColor(), SignedIn = false };
                store.Accounts.Add(a);
                store.Save();
                AddTabFor(a);
                Select(a);
            }
        }

        void Rename(Account a)
        {
            using (var d = new NameDialog("Rename account", "Rename this tab", "Save", a.Name, n => ValidateName(n, a)))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                a.Name = d.NameBox.Text;
                store.Save();
                var t = tabs.First(x => x.Account == a);
                tips.SetToolTip(t, a.Name);
                if (active == a) Text = a.Name + " - " + Brand.Name;
                LayoutBar();
            }
        }

        void MoveTab(Account a, int delta)
        {
            int i = store.Accounts.IndexOf(a), j = i + delta;
            if (j < 0 || j >= store.Accounts.Count) return;
            store.Accounts.RemoveAt(i); store.Accounts.Insert(j, a);
            var t = tabs[i]; tabs.RemoveAt(i); tabs.Insert(j, t);
            store.Save();
            LayoutBar();
        }

        void Remove(Account a)
        {
            using (var d = new DeleteDialog(a))
                if (d.ShowDialog(this) != DialogResult.OK) return;
            int index = store.Accounts.IndexOf(a);
            var page = pages[a.Id];
            page.Shutdown();
            content.Controls.Remove(page);
            page.Dispose();
            pages.Remove(a.Id);
            var t = tabs.First(x => x.Account == a);
            tabArea.Controls.Remove(t); tabs.Remove(t); t.Dispose();
            store.Accounts.Remove(a);
            store.PendingDelete.Add(Paths.BrowserData(a.Id));
            store.Save();
            DeletePendingSoon();
            if (store.Accounts.Count == 0) ShowEmpty();
            else Select(store.Accounts[Math.Min(index, store.Accounts.Count - 1)]);
        }

        // Browser data folders unlock a few seconds after their tab closes.
        void DeletePendingSoon()
        {
            var list = store.PendingDelete.ToList();
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(4000);
                var done = new List<string>();
                foreach (var dir in list)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); done.Add(dir); break; }
                        catch { Thread.Sleep(2000); }
                    }
                }
                try { BeginInvoke((MethodInvoker)delegate { store.PendingDelete.RemoveAll(done.Contains); store.Save(); }); } catch { }
            });
        }

        public void CleanUpOnStart() { if (store.PendingDelete.Count > 0) DeletePendingSoon(); }

        string ValidateName(string n, Account self)
        {
            if (n.Length == 0) return "Please type a name for this account.";
            if (store.Accounts.Any(x => x != self && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase)))
                return "There's already a tab called \"" + n + "\". Please choose a different name.";
            return null;
        }

        string NewId()
        {
            var chars = "abcdefghijkmnpqrstuvwxyz23456789";
            var rnd = new Random();
            string id;
            do
            {
                var sb = new StringBuilder("acct-");
                for (int i = 0; i < 6; i++) sb.Append(chars[rnd.Next(chars.Length)]);
                id = sb.ToString();
            } while (store.Accounts.Any(a => a.Id == id) || Directory.Exists(Paths.BrowserData(id)));
            return id;
        }

        int NextColor()
        {
            for (int i = 0; i < Look.Palette.Length; i++)
                if (!store.Accounts.Any(a => a.ColorIndex == i)) return i;
            return store.Accounts.Count % Look.Palette.Length;
        }

        void ShowAbout() { using (var d = new AboutDialog()) d.ShowDialog(this); }

        void ShowHelp()
        {
            MessageBox.Show(this,
                "Switching accounts\n" +
                "Click a tab, or press Ctrl+1 to Ctrl+9. Ctrl+Tab moves to the next tab.\n\n" +
                "Adding an account\n" +
                "Click + (or press Ctrl+T), type a name, then sign in inside the new tab with that account's email.\n\n" +
                "Sign-in links by email\n" +
                "If Claude emails you a sign-in link, don't click it (it would open your normal browser). Right-click the link, choose Copy link, then click \"Paste sign-in link\" in the tab.\n\n" +
                "Deleting an account\n" +
                "Right-click its tab and choose Delete account. This signs it out on this PC and removes its data here. The Claude account itself isn't deleted.\n\n" +
                "Staying signed in\n" +
                "Your tabs and sign-ins are saved on this PC, so they're still there after you close the app or restart. Claude may occasionally ask you to sign in again.\n\n" +
                "Good to know\n" +
                "\u2022 Links inside chats open in your normal browser.\n" +
                "\u2022 This shows the web version of Claude. Desktop-only features such as Cowork aren't available in tabs.\n" +
                "\u2022 Google sign-in may refuse to work inside apps. Use email sign-in instead.\n\n" +
                "Data on this PC: " + Paths.AppData + "\n\n" +
                Brand.Disclaimer,
                Brand.Name + " - Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- keyboard

        public void UpdateNav(AccountPage p)
        {
            if (p != null && p == CurrentPage()) backBtn.Enabled = p.CanGoBack;
        }

        public void HandleShortcut(string m)
        {
            if (m == "add") { AddAccount(); return; }
            if (store.Accounts.Count == 0) return;
            if ((m == "next" || m == "prev") && active != null)
            {
                int i = store.Accounts.IndexOf(active);
                int j = (i + (m == "prev" ? -1 : 1) + store.Accounts.Count) % store.Accounts.Count;
                Select(store.Accounts[j]);
                return;
            }
            int n;
            if (m.StartsWith("tab:") && int.TryParse(m.Substring(4), out n) && n >= 1 && n <= store.Accounts.Count)
                Select(store.Accounts[n - 1]);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0;
            bool shift = (keyData & Keys.Shift) != 0;
            if (ctrl && key == Keys.T) { AddAccount(); return true; }
            if (ctrl && key == Keys.Tab && store.Accounts.Count > 1 && active != null)
            {
                int i = store.Accounts.IndexOf(active);
                int j = (i + (shift ? -1 : 1) + store.Accounts.Count) % store.Accounts.Count;
                Select(store.Accounts[j]);
                return true;
            }
            if (ctrl && key >= Keys.D1 && key <= Keys.D9)
            {
                int i = key - Keys.D1;
                if (i < store.Accounts.Count) { Select(store.Accounts[i]); return true; }
            }
            if (key == Keys.F5 && CurrentPage() != null) { CurrentPage().Reload(); return true; }
            bool alt = (keyData & Keys.Alt) != 0;
            if (alt && !ctrl && key == Keys.Left && CurrentPage() != null) { CurrentPage().GoBack(); return true; }
            if (alt && !ctrl && key == Keys.Home && CurrentPage() != null) { CurrentPage().GoHome(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- window position

        void RestoreBounds2()
        {
            var r = new Rectangle(store.X, store.Y, store.Width, store.Height);
            bool onScreen = r.Width > 300 && r.Height > 200 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r));
            if (onScreen) Bounds = r;
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                var w = Math.Min(wa.Width - 80, Ui.S(1280)); var h = Math.Min(wa.Height - 60, Ui.S(860));
                Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
            }
            if (store.Maximized) WindowState = FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            store.Maximized = WindowState == FormWindowState.Maximized;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            store.X = b.X; store.Y = b.Y; store.Width = b.Width; store.Height = b.Height;
            store.Save();
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------------ start-menu shortcut (standard shell API)

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    static class Shortcut
    {
        // Keeps a Start menu entry pointing at wherever the app is kept.
        public static void SyncStartMenu()
        {
            try
            {
                var link = (IShellLinkW)new CShellLink();
                try
                {
                    link.SetPath(Paths.ThisExe);
                    link.SetWorkingDirectory(Paths.ThisDir);
                    link.SetDescription("Use several Claude accounts in tabs - by Gerizon Technologies");
                    link.SetIconLocation(Paths.ThisExe, 0);
                    ((IPersistFile)link).Save(Paths.StartMenuLink, true);
                }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ start-up

    static class Program
    {
        const string MutexName = "Local\\GerizonClaudeAccounts.Tabs";
        const string ShowEventName = "Local\\GerizonClaudeAccounts.Tabs.Show";

        // Use the WebView2 loader that matches this PC (x64, x86 or ARM64), shipped next to the app.
        // Kept in its own method so the WebView2 library loads only after the files check.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void UseMatchingLoader()
        {
            try
            {
                var arch = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "AMD64").ToUpperInvariant();
                var rid = arch == "ARM64" ? "win-arm64" : (Environment.Is64BitProcess ? "win-x64" : "win-x86");
                var loaderDir = Path.Combine(Paths.ThisDir, "runtimes", rid, "native");
                if (File.Exists(Path.Combine(loaderDir, "WebView2Loader.dll"))) CoreWebView2Environment.SetLoaderDllFolderPath(loaderDir);
            }
            catch { }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static Form CreateMainForm()
        {
            var f = new MainForm();
            f.CleanUpOnStart();
            return f;
        }

        [STAThread]
        static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = Math.Max(1f, g.DpiX / 96f);

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            {
                if (!createdNew) { showEvent.Set(); return 0; }

                if (!File.Exists(Path.Combine(Paths.ThisDir, "Microsoft.Web.WebView2.Core.dll")))
                {
                    MessageBox.Show("Some of the app's files are missing. Unzip the whole \"" + Brand.Name + "\" folder (not just the .exe), then open the app from inside that folder.",
                        Brand.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 1;
                }

                Paths.MigrateFromV2();
                UseMatchingLoader();
                ThreadPool.QueueUserWorkItem(delegate { Shortcut.SyncStartMenu(); });
                Form form = CreateMainForm();
                var listener = new Thread(() =>
                {
                    while (true)
                    {
                        showEvent.WaitOne();
                        try
                        {
                            form.BeginInvoke((MethodInvoker)delegate
                            {
                                if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                                form.Activate();
                            });
                        }
                        catch { return; }
                    }
                }) { IsBackground = true };
                listener.Start();
                Application.Run(form);
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
