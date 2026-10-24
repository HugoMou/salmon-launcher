using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SL.Core.Account;

/// <summary>已登录的微软/正版会话(持久化到 accounts\msa.json)。</summary>
public sealed record MsSession
{
    public string MicrosoftAccessToken { get; init; } = "";
    public string MicrosoftRefreshToken { get; init; } = "";
    public string MinecraftAccessToken { get; init; } = "";
    public string Uuid { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>minecraftservices 令牌过期时刻(UTC)。</summary>
    public DateTimeOffset MinecraftExpiresAt { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool MinecraftValid => MinecraftAccessToken.Length > 0 && DateTimeOffset.Now < MinecraftExpiresAt - TimeSpan.FromSeconds(300);
}

public sealed class MsAuthException(string message) : Exception(message);

/// <summary>
/// 阶段八:微软正版登录。用微软一方 Xbox 公共 client id(与官方启动器一致)+ 设备码流程,
/// 链路:MS OAuth → Xbox Live(RPS) → XSTS → minecraftservices → profile。全部真实 HTTP。
/// </summary>
public static class MsAuth
{
    private const string ClientId = "00000000402b5328";
    private const string Scope = "XboxLive.signin offline_access";

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan, // 轮询由调用方控制
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SalmonLauncher/0.3 (Salmon Launcher Launcher)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>交互式授权请求(官方启动器同款:PKCE + oauth20_desktop.srf 回跳)。</summary>
    public sealed record AuthorizationRequest(string Url, string Verifier);

    public static AuthorizationRequest BeginInteractive()
    {
        var verifier = Base64UrlRandom(48);
        var challenge = Base64Url(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize"
                  + "?client_id=" + ClientId
                  + "&response_type=code"
                  + "&redirect_uri=" + Uri.EscapeDataString("https://login.live.com/oauth20_desktop.srf")
                  + "&response_mode=query"
                  + "&scope=" + Uri.EscapeDataString(Scope)
                  + "&prompt=select_account"
                  + "&code_challenge=" + challenge
                  + "&code_challenge_method=S256";
        return new AuthorizationRequest(url, verifier);
    }

    /// <summary>用授权码完成令牌交换,并续跑全链路(XBL → XSTS → minecraft → profile)。</summary>
    public static async Task<MsSession> CompleteInteractiveAsync(string code, string verifier, Action<string>? step = null, CancellationToken ct = default)
    {
        step?.Invoke("正在交换访问令牌…");
        string msAccess, msRefresh;
        using (var req = new HttpRequestMessage(HttpMethod.Post,
                   "https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
        {
            Content = BuildForm(
                ("client_id", ClientId),
                ("grant_type", "authorization_code"),
                ("code", code),
                ("redirect_uri", "https://login.live.com/oauth20_desktop.srf"),
                ("code_verifier", verifier)),
        })
        {
            using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
                throw new MsAuthException("令牌交换失败:" + (err.GetString() ?? "") + " / " + (root.TryGetProperty("error_description", out var de) ? de.GetString() : ""));
            msAccess = root.GetProperty("access_token").GetString() ?? "";
            msRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
            if (msAccess.Length == 0)
                throw new MsAuthException("令牌交换失败:响应缺少 access_token");
        }

        return await FinishAsync(msAccess, msRefresh, step, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从浏览器回跳地址中提取授权码;也接受用户直接粘贴的 code。
    /// 回跳形如 https://login.live.com/oauth20_desktop.srf?code=...&amp;state=...
    /// </summary>
    public static string ExtractAuthorizationCode(string input)
    {
        var text = (input ?? "").Trim().Trim('"', '\'');
        if (text.Length == 0)
            throw new MsAuthException("没有收到授权码;请复制浏览器地址栏的完整回调链接。");

        var q = text.IndexOf('?');
        if (q < 0)
        {
            if (text.Contains("://", StringComparison.Ordinal))
                throw new MsAuthException("该链接缺少查询参数;请复制登录完成后地址栏里的完整链接。");
            return text; // 直接粘贴 code
        }

        var query = text[(q + 1)..];
        var hash = query.IndexOf('#');
        if (hash >= 0)
            query = query[..hash];

        var error = QueryValue(query, "error_description") ?? QueryValue(query, "error");
        if (!string.IsNullOrEmpty(error))
            throw new MsAuthException("微软授权失败:" + error);

        var code = QueryValue(query, "code");
        if (string.IsNullOrWhiteSpace(code))
            throw new MsAuthException("回调链接里没有 code 参数;请确认复制的是登录完成后的完整地址。");
        return code;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            var name = i < 0 ? pair : pair[..i];
            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = i < 0 ? "" : pair[(i + 1)..];
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return null;
    }

    private static string Base64UrlRandom(int bytesCount)
        => Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(bytesCount));

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ------------------------------------------------------------ 设备码 + 轮询 (主通道:PCL 式大字设备码,无需复制回跳 URL)

    /// <summary>设备码起步:返回 userCode/deviceCode/verificationUri;用户在 verificationUri 输入 userCode 完成授权后,由 LoginAsync 轮询收尾。</summary>
    public static async Task<(string UserCode, string DeviceCode, string VerificationUri, int IntervalSeconds)> StartAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode")
        {
            Content = BuildForm(("client_id", ClientId), ("scope", Scope)),
        };
        using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
            throw new MsAuthException("微软设备码申请失败:" + err.GetString());

        var interval = root.TryGetProperty("interval", out var it) ? it.GetInt32() : 5;
        return (
            root.GetProperty("user_code").GetString() ?? "",
            root.GetProperty("device_code").GetString() ?? "",
            root.GetProperty("verification_uri").GetString() ?? "https://www.microsoft.com/link",
            Math.Clamp(interval, 3, 15));
    }

    private enum PollState { Pending, Denied, Done }

    /// <summary>轮询一次令牌。</summary>
    private static async Task<(PollState State, string? Access, string? Refresh)> PollOnceAsync(
        string deviceCode, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
        {
            Content = BuildForm(
                ("client_id", ClientId),
                ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                ("device_code", deviceCode)),
        };
        using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
        {
            var code = err.GetString();
            if (code == "authorization_pending" || code == "slow_down")
                return (PollState.Pending, null, null);
            return (PollState.Denied, null, null);
        }
        return (PollState.Done,
            root.GetProperty("access_token").GetString(),
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null);
    }

    // ------------------------------------------------------------ 全链路

    /// <summary>
    /// 完整登录:在 interval 节奏下轮询设备码直到授权完成(maxSeconds 上限),
    /// 过了微软这关再串 Xbox Live → XSTS → minecraftservices → profile。
    /// </summary>
    /// <param name="step">每一步的状态回调(展示用)。</param>
    public static async Task<MsSession> LoginAsync(string deviceCode, int intervalSeconds, int maxSeconds,
        Action<string>? step = null, CancellationToken ct = default)
    {
        string? msAccess = null, msRefresh = null;
        var deadline = DateTimeOffset.Now.AddSeconds(maxSeconds);
        while (DateTimeOffset.Now < deadline)
        {
            PollState state;
            try
            {
                (state, msAccess, msRefresh) = await PollOnceAsync(deviceCode, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                state = PollState.Pending; // 轮询期网络抖动不中断等待,持续重试直到截止
            }
            if (state == PollState.Done)
                break;
            if (state == PollState.Denied)
                throw new MsAuthException("微软登录被拒绝或已过期;请在启动器内重新发起登录。");
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct).ConfigureAwait(false);
        }
        if (msAccess is null || msRefresh is null)
            throw new MsAuthException("等待授权超时;请重新发起登录。");

        return await FinishAsync(msAccess, msRefresh, step, ct).ConfigureAwait(false);
    }

    /// <summary>持有微软令牌后的公共链路(XBL → XSTS → minecraftservices → profile → 会话)。</summary>
    private static async Task<MsSession> FinishAsync(
        string msAccess, string msRefresh, Action<string>? step, CancellationToken ct)
    {
        step?.Invoke("正在向 Xbox Live 认证…");
        var (bltToken, uhs) = await XblUserTokenAsync(msAccess, ct).ConfigureAwait(false);

        step?.Invoke("正在获取 XSTS 令牌…");
        var xstsToken = await XstsTokenAsync(bltToken, uhs, ct).ConfigureAwait(false);

        step?.Invoke("正在登录 minecraftservices…");
        var mcAccess = await MinecraftLoginAsync(uhs, xstsToken, ct).ConfigureAwait(false);

        step?.Invoke("正在读取玩家档案…");
        var (uuid, name) = await ProfileAsync(mcAccess, ct).ConfigureAwait(false);

        return new MsSession
        {
            MicrosoftAccessToken = msAccess,
            MicrosoftRefreshToken = msRefresh,
            MinecraftAccessToken = mcAccess,
            Uuid = uuid,
            Name = name,
            MinecraftExpiresAt = DateTimeOffset.Now + TimeSpan.FromSeconds(MinecraftTokenLifetimeSeconds),
        };
    }

    private const int MinecraftTokenLifetimeSeconds = 86400 * 3; // minecraftservices 有效期约 3 天,保守记满

    /// <summary>会话保活:未过期直接返回;过期则用 refresh_token 走一轮新链。</summary>
    public static async Task<MsSession> EnsureFreshAsync(MsSession session, Action<string>? step = null, CancellationToken ct = default)
    {
        while (!session.MinecraftValid)
        {
            session = await RefreshChainAsync(session, step, ct).ConfigureAwait(false);
        }
        return session;
    }

    private static async Task<MsSession> RefreshChainAsync(MsSession session, Action<string>? step, CancellationToken ct)
    {
        // 1) MSA refresh_token 换新微软令牌
        step?.Invoke("正在续签微软令牌…");
        string msAccess, msRefresh;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
            {
                Content = BuildForm(
                    ("client_id", ClientId),
                    ("grant_type", "refresh_token"),
                    ("refresh_token", session.MicrosoftRefreshToken),
                    ("scope", Scope)),
            };
            using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
                throw new MsAuthException("微软会话已失效(" + err.GetString() + ");请重新登录。");
            msAccess = root.GetProperty("access_token").GetString() ?? "";
            msRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? session.MicrosoftRefreshToken : session.MicrosoftRefreshToken;
            if (msAccess.Length == 0)
                throw new MsAuthException("微软会话已失效;请重新登录。");
        }
        catch (MsAuthException) { throw; }

        // 2) 复用公共链路续签(内部重新走 XBL → profile)
        return await FinishAsync(msAccess, msRefresh, step, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ 各站

    private static async Task<(string XboxToken, string UserHash)> XblUserTokenAsync(string msAccess, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://user.auth.xboxlive.com/user/authenticate")
        {
            Content = JsonBody(new
            {
                Properties = new
                {
                    AuthMethod = "RPS",
                    SiteName = "user.auth.xboxlive.com",
                    RpsTicket = "d=" + msAccess,
                },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT",
            }),
        };
        using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
        var token = doc.RootElement.GetProperty("Token").GetString() ?? "";
        var uhs = doc.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString() ?? "";
        if (token.Length == 0 || uhs.Length == 0)
            throw new MsAuthException("Xbox Live 认证失败:该微软账户可能未关联 Xbox 档案。");
        return (token, uhs);
    }

    private static async Task<string> XstsTokenAsync(string bltToken, string uhs, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://xsts.auth.xboxlive.com/xsts/authorize")
        {
            Content = JsonBody(new
            {
                Properties = new
                {
                    UserTokens = new[] { bltToken },
                    SandboxId = "RETAIL",
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT",
            }),
        };
        using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("XErr", out var xerr))
        {
            var code = xerr.TryGetInt64(out var v) ? v : 0;
            throw new MsAuthException(code switch
            {
                2148916238 => "该微软账户没有 Minecraft 正版档案(请在 minecraft.net 购买/迁移)。",
                2148916233 or 2148916234 or 2148916235 => "XSTS 拒绝:账户设置需要调整(XblPrivacy/XblSettings)。",
                _ => "XSTS 授权失败(XErr " + code + ")。",
            });
        }
        return root.GetProperty("Token").GetString() ?? "";
    }

    private static async Task<string> MinecraftLoginAsync(string uhs, string xstsToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.minecraftservices.com/authentication/login_with_xbox")
        {
            Content = JsonBody(new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" }),
        };
        using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
            throw new MsAuthException("minecraftservices 登录失败:" + (err.GetString() ?? ""));
        return root.GetProperty("access_token").GetString() ?? "";
    }

    private static async Task<(string Uuid, string Name)> ProfileAsync(string mcAccess, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccess);
        using var doc = await SendJsonAsync(req, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err) || !root.TryGetProperty("id", out var id))
            throw new MsAuthException("读取档案失败:账户可能没有 Java 版档案。");
        return (
            (id.GetString() ?? "").Replace("-", ""),
            root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
    }

    // ------------------------------------------------------------ 会话存储

    /// <summary>msa.json 读写(accounts 目录;原子写)。</summary>
    public static MsSession? LoadSession()
    {
        var path = SessionPath();
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<MsSession>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void SaveSession(MsSession session)
    {
        var path = SessionPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicWriteJson(path, session);
    }

    public static void ClearSession()
    {
        var path = SessionPath();
        if (File.Exists(path))
            File.Delete(path);
    }

    public static string SessionPath() => Path.Combine(Boot.Runtime.Root, "accounts", "msa.json");

    // ------------------------------------------------------------ 基础设施

    private static FormUrlEncodedContent BuildForm(params (string Key, string Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private static StringContent JsonBody(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> SendJsonAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode && bytes.Length == 0)
            throw new MsAuthException($"HTTP {(int)resp.StatusCode} @ {req.RequestUri?.Host}");
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            throw new MsAuthException($"非 JSON 响应 HTTP {(int)resp.StatusCode} @ {req.RequestUri?.Host}");
        }
    }

    private static void AtomicWriteJson(string path, object payload)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
