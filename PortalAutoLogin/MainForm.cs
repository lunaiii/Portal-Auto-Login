using System.Diagnostics;
using System.Drawing;
using System.Net.NetworkInformation;

namespace PortalAutoLogin;

internal sealed class MainForm : Form
{
    private readonly Settings _settings = Settings.Load();
    private readonly TextBox _userName = new() { Width = 290 };
    private readonly TextBox _password = new() { Width = 290, UseSystemPasswordChar = true };
    private readonly CheckBox _startWithWindows = new() { Text = "登录 Windows 后自动运行", AutoSize = true };
    private readonly Label _status = new() { AutoSize = false, Width = 430, Height = 45 };
    private readonly Button _save = new() { Text = "保存并启用", Width = 120, Height = 34 };
    private readonly Button _retry = new() { Text = "立即检查", Width = 100, Height = 34 };
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 20_000 };
    private bool _configured;
    private readonly bool _startHidden;
    private bool _busy;
    private bool _paused;
    private bool _exitRequested;
    private DateTimeOffset _nextAttemptUtc;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        Text = "网络认证自动登录";
        Icon = SystemIcons.Shield;
        ClientSize = new Size(480, 315);
        MinimumSize = new Size(500, 355);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = "192.168.6.2 上网认证",
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 18)
        };
        Controls.Add(title);

        AddLabel("账号", 25, 66);
        _userName.Location = new Point(125, 61);
        Controls.Add(_userName);
        AddLabel("密码", 25, 107);
        _password.Location = new Point(125, 102);
        Controls.Add(_password);

        var hint = new Label
        {
            Text = "已保存密码时，留空表示保持原密码",
            ForeColor = Color.DimGray,
            AutoSize = true,
            Location = new Point(125, 132)
        };
        Controls.Add(hint);

        _startWithWindows.Location = new Point(125, 160);
        Controls.Add(_startWithWindows);
        _save.Location = new Point(125, 191);
        _retry.Location = new Point(258, 191);
        Controls.Add(_save);
        Controls.Add(_retry);

        _status.Location = new Point(25, 239);
        _status.Text = "正在初始化…";
        Controls.Add(_status);

        var portalLink = new LinkLabel
        {
            Text = "打开网页登录页",
            AutoSize = true,
            Location = new Point(25, 285)
        };
        portalLink.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(
            "http://192.168.6.2/ac_portal/default/pc.html?template=default&tabs=pwd&vlanid=0")
        { UseShellExecute = true });
        Controls.Add(portalLink);

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        menu.Items.Add("立即检查", null, async (_, _) => await RunCheckAsync(manual: true));
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "网络认证自动登录",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _userName.Text = _settings.UserName;
        _startWithWindows.Checked = _settings.StartWithWindows;
        try { _configured = !string.IsNullOrWhiteSpace(_settings.UserName) && CredentialStore.HasPassword(); }
        catch (Exception ex) { SetStatus("读取已保存密码失败：" + ex.Message, Color.DarkRed); }
        if (!_configured) SetStatus("请输入账号和密码，保存后自动监测网络。", Color.DarkOrange);
        if (_configured && _startHidden)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }

        _save.Click += (_, _) => SaveSettings();
        _retry.Click += async (_, _) => await RunCheckAsync(manual: true);
        _timer.Tick += async (_, _) => await RunCheckAsync();
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        Shown += async (_, _) =>
        {
            if (_configured && _startHidden)
            {
                HideToTray();
                Opacity = 1;
            }
            _timer.Start();
            await RunCheckAsync();
        };
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });
    }

    private void SaveSettings()
    {
        string user = _userName.Text.Trim();
        string enteredPassword = _password.Text;
        if (user.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(user) > 95)
        {
            MessageBox.Show(this, "请输入有效账号（不超过 95 字节）。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (enteredPassword.Length == 0 && (!_configured || user != _settings.UserName))
        {
            MessageBox.Show(this, "首次设置或更换账号时，请输入密码。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (enteredPassword.Length > 0) CredentialStore.WritePassword(user, enteredPassword);
            _settings.UserName = user;
            _settings.StartWithWindows = _startWithWindows.Checked;
            _settings.Save();
            _password.Clear();
            _configured = true;
            _paused = false;
            _nextAttemptUtc = DateTimeOffset.MinValue;
            SetStatus("已保存。联网后将自动检测并登录。", Color.DarkGreen);
            _ = RunCheckAsync(manual: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunCheckAsync(bool manual = false)
    {
        if (_busy || !_configured) return;
        if (manual)
        {
            _paused = false;
            _nextAttemptUtc = DateTimeOffset.MinValue;
        }
        if (_paused) return;
        _busy = true;
        try
        {
            SetStatus("正在检查网络…", Color.DimGray);
            NetworkCheck check = await PortalClient.CheckNetworkAsync();
            if (check.State == NetworkState.Online)
            {
                SetStatus("已联网，无需登录。", Color.DarkGreen);
                return;
            }
            if (check.State != NetworkState.PortalRequired)
            {
                SetStatus(check.Detail, Color.DimGray);
                return;
            }
            if (DateTimeOffset.UtcNow < _nextAttemptUtc)
            {
                SetStatus("检测到认证页，稍后自动重试。", Color.DarkOrange);
                return;
            }

            string? password = CredentialStore.ReadPassword();
            if (string.IsNullOrEmpty(password))
            {
                _paused = true;
                SetStatus("未找到保存的密码，请重新设置。", Color.DarkRed);
                AlertUser();
                return;
            }

            SetStatus("检测到认证页，正在登录…", Color.DarkOrange);
            LoginResult result = await PortalClient.LoginAsync(_settings.UserName, password);
            if (result.Rejected)
            {
                _paused = true;
                SetStatus("自动登录已暂停：" + result.Detail, Color.DarkRed);
                AlertUser();
                return;
            }
            _nextAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(result.Success ? 60 : 30);
            if (!result.Success)
            {
                SetStatus(result.Detail + "，稍后重试。", Color.DarkOrange);
                return;
            }

            await Task.Delay(2_000);
            NetworkCheck after = await PortalClient.CheckNetworkAsync();
            SetStatus(after.State == NetworkState.Online
                ? "自动登录成功，已联网。"
                : "认证已提交，等待网络开放。",
                after.State == NetworkState.Online ? Color.DarkGreen : Color.DarkOrange);
        }
        catch (Exception ex)
        {
            _nextAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            SetStatus("检查失败：" + ex.Message, Color.DarkOrange);
        }
        finally { _busy = false; }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(async () => await RunCheckAsync()); }
        catch (InvalidOperationException) { }
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = "状态：" + message;
        _status.ForeColor = color;
        _tray?.Text = message.Length > 50 ? message[..50] : message;
    }

    private void AlertUser()
    {
        _tray.BalloonTipTitle = "网络认证自动登录需要处理";
        _tray.BalloonTipText = _status.Text;
        _tray.ShowBalloonTip(5000);
    }

    public void ShowSettings()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ExitApp()
    {
        _exitRequested = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _timer.Stop();
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }
}
