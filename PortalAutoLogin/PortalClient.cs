using System.Net;
using System.Text;
using System.Text.Json;

namespace PortalAutoLogin;

internal enum NetworkState { Online, PortalRequired, OtherNetwork }
internal sealed record NetworkCheck(NetworkState State, string Detail);
internal sealed record LoginResult(bool Success, bool Rejected, string Detail);

internal static class PortalClient
{
    private static readonly Uri ProbeUrl = new("http://www.msftconnecttest.com/connecttest.txt");
    private static readonly Uri PortalPage = new("http://192.168.6.2/ac_portal/default/pc.html?template=default&tabs=pwd&vlanid=0");
    private static readonly Uri LoginUrl = new("http://192.168.6.2/ac_portal/login.php");

    public static async Task<NetworkCheck> CheckNetworkAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var client = NewClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (body.Trim() == "Microsoft Connect Test")
                    return new(NetworkState.Online, "网络已连接");
                if (body.Contains("<title>上网认证系统</title>", StringComparison.Ordinal) &&
                    body.Contains("ac_portal", StringComparison.Ordinal))
                    return new(NetworkState.PortalRequired, "检测到认证页面");
            }

            Uri? location = response.Headers.Location;
            if (location is not null && !location.IsAbsoluteUri)
                location = new Uri(ProbeUrl, location);
            if (location is not null && IsPortalUrl(location))
                return new(NetworkState.PortalRequired, "检测到认证页面");

            return new(NetworkState.OtherNetwork, "当前网络没有出现目标认证页");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new(NetworkState.OtherNetwork, "等待网络连接");
        }
    }

    public static async Task<LoginResult> LoginAsync(string userName, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return new(false, true, "请先保存账号和密码");

        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                UseProxy = false
            };
            using var client = NewClient(handler);

            // The portal may issue an AUTHSESSID cookie on the login page.
            using var pageResponse = await client.GetAsync(PortalPage, cancellationToken);
            if (!pageResponse.IsSuccessStatusCode)
                return new(false, false, "认证页面暂时无法访问");
            string page = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!page.Contains("id=\"password_name\"", StringComparison.Ordinal) ||
                !page.Contains("onPwdLogin()", StringComparison.Ordinal) ||
                !page.Contains("logic_new.js", StringComparison.Ordinal))
                return new(false, true, "认证页面已变化，已停止自动提交密码");

            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var fields = new Dictionary<string, string>
            {
                ["opr"] = "pwdLogin",
                ["userName"] = userName,
                ["pwd"] = EncryptPassword(password, timestamp),
                ["auth_tag"] = timestamp,
                ["rememberPwd"] = "1"
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
            {
                Content = new FormUrlEncodedContent(fields)
            };
            request.Headers.Referrer = PortalPage;
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, false, $"认证服务器返回 HTTP {(int)response.StatusCode}");

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(false, false, "认证服务器返回了意外数据");

            bool success = root.TryGetProperty("success", out var value) &&
                (value.ValueKind == JsonValueKind.True ||
                 (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number != 0));
            if (!success)
            {
                string message = root.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String
                    ? msg.GetString() ?? "登录被拒绝"
                    : "登录被拒绝";
                return new(false, true, message);
            }

            if (root.TryGetProperty("forms", out var forms) && forms.ValueKind is not JsonValueKind.Null and not JsonValueKind.False)
                return new(false, true, "此账号还需要第三方认证，请使用浏览器登录");

            return new(true, false, "认证服务器已接受登录");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(false, false, "认证请求失败，稍后重试");
        }
    }

    internal static string EncryptPassword(string password, string key)
    {
        // Matches do_encrypt_rc4 in this portal's logic_new.js. JavaScript works on UTF-16 code units.
        password = password.Trim();
        var box = Enumerable.Range(0, 256).ToArray();
        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + box[i] + key[i % key.Length]) % 256;
            (box[i], box[j]) = (box[j], box[i]);
        }
        var output = new StringBuilder(password.Length * 2);
        int a = 0, b = 0;
        foreach (char character in password)
        {
            a = (a + 1) % 256;
            b = (b + box[a]) % 256;
            (box[a], box[b]) = (box[b], box[a]);
            int c = (box[a] + box[b]) % 256;
            output.Append((character ^ box[c]).ToString("x2"));
        }
        return output.ToString();
    }

    private static bool IsPortalUrl(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        uri.Host == "192.168.6.2" && uri.AbsolutePath.StartsWith("/ac_portal/", StringComparison.OrdinalIgnoreCase);

    private static HttpClient NewClient(HttpClientHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PortalAutoLogin/1.0");
        return client;
    }
}
