using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.Browser;
using Alife.Function.FunctionCaller;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Function.Mcp.MaoMao;

public class McpConfig
{
    // 市场插件本身无需配置；安装目标为官方 Alife.Function.Mcp.McpService
}

/// <summary>
/// 魔搭 MCP 广场单个服务的商店信息（来自 GET /api/v1/mcpServers/{owner}/{name}）。
/// </summary>
public class McpServerInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string ChineseName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Hosted { get; set; }
    public string Command { get; set; } = "";
    public string[] Arguments { get; set; } = [];
    public int ToolCount { get; set; }
    public string Url { get; set; } = "";
}

/// <summary>
/// 已安装到官方 Alife.Function.Mcp.McpService 的服务器摘要。
/// </summary>
public class McpInstalledInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Address { get; set; } = "";
    public bool Enabled { get; set; }
}

[Module("MCP 商店",
    "浏览魔搭 MCP 广场的 MCP 服务，一键安装到 Alife.Function.Mcp.McpService。",
    defaultCategory: "猫猫的小工具",
    editorUI: typeof(McpUI))]
public class McpModule(
    XmlFunctionCaller functionCaller,
    Interactor<McpModule> interactor) :
    ChatBehaviour,
    IConfigurable<McpConfig>
{
    public McpConfig Configuration { get; set; } = null!;

    private static readonly string McpServiceConfigPath = Path.Combine(
        AlifePath.StorageFolderPath, "Configuration", "Alife.Function.Mcp.McpService.json");

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this)
        {
            Description = "MCP 商店：从魔搭 MCP 广场浏览并安装 MCP 服务到 Alife.Function.Mcp.McpService。",
        };
        functionCaller.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("从魔搭 MCP 广场安装一个 MCP 服务到 Alife.Function.Mcp.McpService")]
    public async Task InstallMcpServer(
        [Description("服务 owner（如 @modelcontextprotocol）")] string owner,
        [Description("服务名称（如 fetch）")] string name)
    {
        try
        {
            McpServerInfo info = await FetchMcpServerDetailAsync(owner, name);
            InstallToMcpService(info);
            string shown = string.IsNullOrWhiteSpace(info.ChineseName) ? info.Name : info.ChineseName;
            interactor.Poke($"喵，已安装 MCP 服务「{shown}」到 Alife.Function.Mcp.McpService，重载后生效。");
        }
        catch (Exception ex)
        {
            interactor.Poke($"喵，安装 MCP 服务失败：{ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列出已安装到 Alife.Function.Mcp.McpService 的 MCP 服务器")]
    public Task ListInstalledMcpServers()
    {
        List<McpInstalledInfo> servers = ListInstalledFromMcpService();
        if (servers.Count == 0)
        {
            interactor.Poke("喵，还没有安装 MCP 服务器。");
            return Task.CompletedTask;
        }

        var lines = new List<string>();
        foreach (McpInstalledInfo server in servers)
        {
            lines.Add($"{server.Name}（{(server.Enabled ? "启用" : "停用")}，{server.Type}：{server.Address}）");
        }
        interactor.Poke("喵，已安装的 MCP 服务器：\n- " + string.Join("\n- ", lines));
        return Task.CompletedTask;
    }

    public static async Task<McpServerInfo> FetchMcpServerDetailAsync(string owner, string name)
    {
        string url = BuildMcpDetailUrl(owner, name);
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Alife-McpStore");

        string json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Data", out JsonElement data) == false ||
            data.ValueKind != JsonValueKind.Object)
        {
            throw new Exception("未找到该 MCP 服务，请确认 owner/name 是否正确。");
        }

        var info = new McpServerInfo
        {
            Name = GetProp(data, "Name"),
            Path = GetProp(data, "Path"),
            ChineseName = GetProp(data, "ChineseName"),
            Description = GetProp(data, "AbstractCN"),
            Hosted = data.TryGetProperty("Hosted", out JsonElement hosted) && hosted.ValueKind == JsonValueKind.True,
            Url = $"https://www.modelscope.cn/mcp/servers/{GetProp(data, "Path")}/{GetProp(data, "Name")}"
        };
        if (string.IsNullOrWhiteSpace(info.Name))
            throw new Exception("未找到该 MCP 服务，请确认 owner/name 是否正确。");

        if (data.TryGetProperty("Tools", out JsonElement tools) && tools.ValueKind == JsonValueKind.Array)
            info.ToolCount = tools.GetArrayLength();

        // stdio 配置：ServerConfig = [{ mcpServers: { name: { command, args } } }]
        if (data.TryGetProperty("ServerConfig", out JsonElement serverConfig) &&
            serverConfig.ValueKind == JsonValueKind.Array &&
            serverConfig.GetArrayLength() > 0)
        {
            TryParseStdioConfig(serverConfig[0], info);
        }
        return info;
    }

    /// <summary>
    /// 用真实浏览器（WebView2，可过魔搭反爬）打开魔搭 MCP 广场，读取渲染后的服务列表。
    /// 会短暂弹出浏览器窗口，读取完成后自动关闭。
    /// </summary>
    /// <summary>
    /// 用真实浏览器（WebView2，可过魔搭反爬）打开魔搭 MCP 广场指定页（可带搜索关键字），
    /// 读取渲染后的服务列表。会短暂弹出浏览器窗口，读取完成后自动关闭。
    /// </summary>
    public static async Task<List<McpServerInfo>> FetchMarketplaceMcpServersPageAsync(string? keyword, int page)
    {
        var result = new List<McpServerInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var engine = new BrowserEngine();
        await engine.WaitToLoadedAsync(TimeSpan.FromSeconds(15));

        string url = "https://modelscope.cn/mcp?page=" + page;
        if (string.IsNullOrWhiteSpace(keyword) == false)
            url += "&name=" + Uri.EscapeDataString(keyword.Trim());
        _ = await engine.OpenWebsiteAsync(url);

        string collectJs = "JSON.stringify(Array.from(document.querySelectorAll('a[href*=\"/mcp/servers/\"]')).map(a => ({ href: a.getAttribute('href') || '', text: (a.innerText || a.textContent || '').trim() })).filter(x => x.href.includes('/mcp/servers/') && !x.href.endsWith('/create')))";

        // 读当前页，等待 SPA 异步加载
        for (int attempt = 0; attempt < 6; attempt++)
        {
            string raw = await engine.RunWebsiteJsAsync(collectJs);
            string json = ExtractJsResult(raw);
            if (json.StartsWith("[") && json.Contains("\"href\""))
            {
                MergeMarketplaceJson(json, result, seen);
                if (result.Count > 0)
                    break;
            }
            await Task.Delay(2000);
        }
        return result;
    }

    private static void MergeMarketplaceJson(string json, List<McpServerInfo> result, HashSet<string> seen)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            string href = GetProp(item, "href");
            string text = GetProp(item, "text");
            int idx = href.IndexOf("/mcp/servers/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            string key = href.Substring(idx + "/mcp/servers/".Length).TrimEnd('/');
            int slash = key.IndexOf('/');
            if (slash <= 0 || slash == key.Length - 1)
                continue;

            string owner = key.Substring(0, slash);
            string name = key.Substring(slash + 1);
            string dedup = owner + "/" + name;
            if (seen.Add(dedup) == false)
                continue;

            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string display = lines.Length > 0 ? lines[0].Trim() : name;
            string desc = string.Join(" ", lines.Skip(1)).Trim();
            if (desc.Length > 120)
                desc = desc.Substring(0, 120) + "…";

            result.Add(new McpServerInfo
            {
                Name = name,
                Path = owner,
                ChineseName = display,
                Description = desc,
                Url = $"https://www.modelscope.cn/mcp/servers/{owner}/{name}"
            });
        }
    }

    private static readonly string[] GithubRawProxies =
    {
        "https://ghproxy.net/",
        "https://gh-proxy.com/",
        "https://ghfast.top/"
    };

    /// <summary>
    /// 从 GitHub 社区清单 awesome-mcp-servers 获取 MCP 服务器列表（经代理，避开 GitHub 直连被墙）。
    /// </summary>
    public static async Task<List<McpServerInfo>> FetchGithubMcpListAsync()
    {
        const string rawUrl = "https://raw.githubusercontent.com/punkpeye/awesome-mcp-servers/main/README.md";

        string content = "";
        Exception? lastError = null;
        foreach (string proxy in GithubRawProxies)
        {
            try
            {
                content = await GetWithProxyAsync(proxy + rawUrl);
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        if (string.IsNullOrWhiteSpace(content))
            throw new Exception($"获取 GitHub MCP 清单失败：{lastError?.Message ?? "代理均不可用"}");

        var result = new List<McpServerInfo>();
        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("- [") == false)
                continue;

            int closeBracket = trimmed.IndexOf((char)93); // ']'
            if (closeBracket < 3)
                continue;
            int openParen = trimmed.IndexOf((char)40, closeBracket); // '('
            if (openParen < 0)
                continue;
            int closeParen = trimmed.IndexOf((char)41, openParen); // ')'
            if (closeParen <= openParen)
                continue;

            string display = trimmed.Substring(3, closeBracket - 3).Trim();
            string url = trimmed.Substring(openParen + 1, closeParen - openParen - 1).Trim();
            if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) == false)
                continue;

            string rest = trimmed.Substring(closeParen + 1);
            StripMarkdownLinks(ref rest);
            int sep = rest.IndexOf(" - ", StringComparison.Ordinal);
            if (sep >= 0)
                rest = rest.Substring(sep + 3);
            rest = Regex.Replace(rest, @"\s+", " ").Trim(' ', '-', '•', '|');
            if (rest.Length > 120)
                rest = rest.Substring(0, 120) + "…";

            string path = url.Substring("https://github.com/".Length).TrimEnd('/');
            int slash = path.IndexOf('/');
            if (slash <= 0 || slash == path.Length - 1)
                continue;
            string owner = path.Substring(0, slash);
            string name = path.Substring(slash + 1);

            result.Add(new McpServerInfo
            {
                Name = name,
                Path = owner,
                ChineseName = display,
                Description = rest,
                Url = url
            });
        }
        return result;
    }

    /// <summary>去掉文本中的 markdown 链接 [..](..)（含徽章图片链接）。</summary>
    private static void StripMarkdownLinks(ref string text)
    {
        int idx = 0;
        while (true)
        {
            int close = text.IndexOf((char)93, idx); // ']'
            if (close < 0 || close + 1 >= text.Length || text[close + 1] != (char)40) // '('
                break;
            int start = text.LastIndexOf((char)91, close); // '['
            if (start < 0)
                break;
            int end = text.IndexOf((char)41, close + 1); // ')'
            if (end < 0)
                break;
            text = text.Remove(start, end - start + 1);
            idx = start;
        }
    }

    private static async Task<string> GetWithProxyAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) == false ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new Exception("代理地址无效");
        }

        string host = uri.Host;
        if (host.EndsWith("ghproxy.net", StringComparison.OrdinalIgnoreCase) == false &&
            host.EndsWith("gh-proxy.com", StringComparison.OrdinalIgnoreCase) == false &&
            host.EndsWith("ghfast.top", StringComparison.OrdinalIgnoreCase) == false)
        {
            throw new Exception($"不允许的代理主机：{host}");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Alife-McpStore");
        return await http.GetStringAsync(url);
    }

    private static string ExtractJsResult(string raw)
    {
        int idx = raw.IndexOf("Return:\n", StringComparison.Ordinal);
        string s = idx >= 0 ? raw.Substring(idx + "Return:\n".Length) : raw;
        return s.Trim();
    }

    /// <summary>
    /// 把商店选中的服务写入官方 Alife.Function.Mcp.McpService 的配置文件。
    /// </summary>
    public static void InstallToMcpService(McpServerInfo info)
    {
        string name = string.IsNullOrWhiteSpace(info.ChineseName) ? info.Name : info.ChineseName;

        JObject config = LoadOrCreateMcpServiceConfig();
        JToken? serversToken = config["Servers"];
        if (serversToken is not JArray servers)
        {
            servers = new JArray();
            config["Servers"] = servers;
        }

        foreach (JObject existing in servers.OfType<JObject>().ToList())
        {
            if (string.Equals(existing["Name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                servers.Remove(existing);
        }

        var server = new JObject
        {
            ["Enabled"] = info.Hosted == false && string.IsNullOrWhiteSpace(info.Command) == false, // 有启动命令才自动启用
            ["Name"] = name,
            ["Description"] = info.Description,
            ["Command"] = info.Command,
            ["Arguments"] = new JArray(info.Arguments),
            ["IsImplicit"] = true
        };
        if (info.Hosted)
            server["Endpoint"] = "https://";

        servers.Add(server);
        SaveMcpServiceConfig(config);
    }

    public static List<McpInstalledInfo> ListInstalledFromMcpService()
    {
        var result = new List<McpInstalledInfo>();
        try
        {
            JObject config = LoadOrCreateMcpServiceConfig();
            if (config["Servers"] is JArray servers)
            {
                foreach (JToken token in servers)
                {
                    if (token is not JObject obj)
                        continue;
                    string endpoint = obj["Endpoint"]?.ToString() ?? "";
                    bool isUrl = string.IsNullOrWhiteSpace(endpoint) == false;
                    string address = isUrl ? endpoint : obj["Command"]?.ToString() ?? "";
                    result.Add(new McpInstalledInfo
                    {
                        Name = obj["Name"]?.ToString() ?? "",
                        Type = isUrl ? "URL 服务" : "本地命令",
                        Address = address,
                        Enabled = obj["Enabled"]?.Value<bool>() ?? false
                    });
                }
            }
        }
        catch
        {
            // 配置读取失败按空列表处理
        }
        return result;
    }

    public static void RemoveFromMcpService(string name)
    {
        JObject config = LoadOrCreateMcpServiceConfig();
        if (config["Servers"] is JArray servers)
        {
            List<JObject> toRemove = servers.OfType<JObject>()
                .Where(o => string.Equals(o["Name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (JObject obj in toRemove)
                servers.Remove(obj);
            SaveMcpServiceConfig(config);
        }
    }

    private static JObject LoadOrCreateMcpServiceConfig()
    {
        try
        {
            if (File.Exists(McpServiceConfigPath))
                return JObject.Parse(File.ReadAllText(McpServiceConfigPath));
        }
        catch
        {
            // 损坏的配置按空配置处理
        }
        return new JObject { ["Servers"] = new JArray() };
    }

    private static void SaveMcpServiceConfig(JObject config)
    {
        string? dir = Path.GetDirectoryName(McpServiceConfigPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(McpServiceConfigPath, config.ToString(Formatting.Indented));
    }

    private static void TryParseStdioConfig(JsonElement config, McpServerInfo info)
    {
        if (config.TryGetProperty("mcpServers", out JsonElement servers) == false ||
            servers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty prop in servers.EnumerateObject())
        {
            if (prop.Value.TryGetProperty("command", out JsonElement cmd) && cmd.ValueKind == JsonValueKind.String)
                info.Command = cmd.GetString() ?? "";
            if (prop.Value.TryGetProperty("args", out JsonElement args) && args.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (JsonElement a in args.EnumerateArray())
                {
                    if (a.ValueKind == JsonValueKind.String)
                        list.Add(a.GetString() ?? "");
                }
                info.Arguments = list.ToArray();
            }
            break;
        }
    }

    private static string BuildMcpDetailUrl(string owner, string name)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("缺少 MCP 服务的 owner/name。");

        // 只允许字母数字与 @ - _ . ，拒绝 / 和 .. 等路径注入
        foreach (string part in new[] { owner, name })
        {
            foreach (char c in part)
            {
                bool ok = char.IsLetterOrDigit(c) || c == '@' || c == '-' || c == '_' || c == '.';
                if (!ok)
                    throw new ArgumentException($"非法的 MCP 服务标识：{part}");
            }
        }
        return "https://modelscope.cn/api/v1/mcpServers/" +
               Uri.EscapeDataString(owner) + "/" + Uri.EscapeDataString(name);
    }

    private static string GetProp(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out JsonElement p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? "";
        return "";
    }
}
