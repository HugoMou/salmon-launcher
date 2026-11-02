// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SL.Core.Account;

/// <summary>Signed-in Microsoft/premium session (persisted to accounts\msa.json).</summary>
public sealed record MsSession
{
    public string MicrosoftAccessToken { get; init; } = "";
    public string MicrosoftRefreshToken { get; init; } = "";
    public string MinecraftAccessToken { get; init; } = "";
    public string Uuid { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Expiry time of the minecraftservices token (UTC).</summary>
    public DateTimeOffset MinecraftExpiresAt { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool MinecraftValid => MinecraftAccessToken.Length > 0 && DateTimeOffset.Now < MinecraftExpiresAt - TimeSpan.FromSeconds(300);
}

public sealed class MsAuthException(string message) : Exception(message);

/// <summary>
/// Stage 8: Microsoft premium sign-in. Uses Microsoft first-party Xbox public client id (the same one the official launcher uses) plus the device code flow,
/// chain: MS OAuth → Xbox Live (RPS) → XSTS → minecraftservices → profile. Everything is real HTTP.
/// </summary>
public static class MsAuth
{
    private const string ClientId = "0add8caf-2cc6-4546-b798-c3d171217dd9";
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
            Timeout = Timeout.InfiniteTimeSpan, // Polling is controlled by the caller
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SalmonLauncher/0.3 (SalmonLauncher Launcher)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>Interactive authorization request (same as the official launcher: PKCE + oauth20_desktop.srf redirect).</summary>
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

    /// <summary>Exchange the authorization code for tokens, then continue the whole chain (XBL → XSTS → minecraft → profile).</summary>
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
    /// Extract the authorization code from the browser redirect URL; also accepts a code the user pasted directly.
    /// The redirect looks like https://login.live.com/oauth20_desktop.srf?code=...&amp;state=...
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
            return text; // code pasted directly
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

    // ------------------------------------------------------------ Device code + polling (main path: PCL-style large device code, no need to copy the redirect URL)

    /// <summary>Start the device code flow: returns userCode/deviceCode/verificationUri; once the user enters userCode at verificationUri and authorizes, LoginAsync polls to finish.</summary>
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

    /// <summary>Poll for a token once.</summary>
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

    // ------------------------------------------------------------ Full chain

    /// <summary>
    /// Full sign-in: poll the device code at the interval rhythm until authorization completes (bounded by maxSeconds),
    /// then, past the Microsoft stage, run Xbox Live → XSTS → minecraftservices → profile in sequence.
    /// </summary>
    /// <param name="step">Status callback for each step (for display).</param>
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
                state = PollState.Pending; // Network hiccups while polling do not abort the wait; keep retrying until the deadline
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

    /// <summary>Shared chain once a Microsoft token is held (XBL → XSTS → minecraftservices → profile → session).</summary>
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

    private const int MinecraftTokenLifetimeSeconds = 86400 * 3; // minecraftservices tokens last about 3 days; record the full value conservatively

    /// <summary>Session keep-alive: return as-is when not expired; otherwise run a fresh chain with refresh_token.</summary>
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
        // 1) Exchange the MSA refresh_token for a new Microsoft token
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

        // 2) Reuse the shared chain to renew (internally reruns XBL → profile)
        return await FinishAsync(msAccess, msRefresh, step, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ Each hop

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
        return Prop(root, "Token", "XSTS 授权失败");
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

    // ------------------------------------------------------------ Session storage

    /// <summary>msa.json read/write (accounts directory; atomic write).</summary>
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

    // ------------------------------------------------------------ Infrastructure

    /// <summary>Read a string field safely; when missing or empty, throw a friendly exception with context so a raw KeyNotFoundException never reaches the UI.</summary>
    private static string Prop(JsonElement el, string name, string context)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrEmpty(s))
                return s;
        }
        throw new MsAuthException(context + ":服务器响应缺少必要字段,请稍后重试;若反复出现,该账户可能无法完成此认证步骤。");
    }

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
