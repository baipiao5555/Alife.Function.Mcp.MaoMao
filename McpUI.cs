using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Alife.Function.Mcp.MaoMao;

public sealed class McpUI : ModuleUIBase<McpModule, McpConfig>
{
    private string _storeInput = "https://modelscope.cn/mcp";
    private McpServerInfo? _storeInfo;
    private List<McpServerInfo>? _marketplaceList;
    private List<McpServerInfo>? _githubList;
    private string _marketplaceSearch = "";
    private string _githubSearch = "";
    private string _storeStatus = "";
    private bool _storeLoading;
    private int _marketplacePage;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (Configuration == null)
        {
            b.AddContent(0, "Configuration NULL");
            return;
        }

        int i = 0;
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "style",
            "display:flex;flex-direction:column;width:100%;box-sizing:border-box;min-width:0;padding-bottom:8px;");

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "style", "font-size:16px;font-weight:700;margin-bottom:8px;");
        b.AddContent(i++, "MCP 商店");
        b.CloseElement();

        Hint(b, ref i,
            "从魔搭 MCP 广场浏览并安装 MCP 服务，安装后自动写入官方 Alife.Function.Mcp.McpService 配置，重载插件后由官方插件连接。");

        SectionTitle(b, ref i, "商店（魔搭 MCP 广场）");
        Hint(b, ref i,
            "默认加载魔搭 MCP 广场：点「加载」会用内置浏览器打开广场并自动读取服务列表（会短暂弹出浏览器窗口，读取后自动关闭），列出后点「安装」即可。也可直接粘贴单个服务详情页地址（https://modelscope.cn/mcp/servers/owner/name）加载。");

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "style", "display:flex;gap:6px;align-items:center;");
        b.OpenElement(i++, "input");
        b.AddAttribute(i++, "type", "text");
        b.AddAttribute(i++, "value", _storeInput);
        b.AddAttribute(i++, "placeholder", "MCP 服务详情页 URL 或 owner/name");
        b.AddAttribute(i++, "style",
            "flex:1;min-width:0;box-sizing:border-box;padding:6px 9px;border:1px solid #d9d9d9;border-radius:6px;font-size:12px;");
        b.AddAttribute(i++, "onchange",
            EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
            {
                if (e.Value is string s)
                    _storeInput = s;
            }));
        b.CloseElement();
        AddButton(b, ref i, "加载", () => { _ = LoadStoreServerAsync(); });
        AddButton(b, ref i, "加载 GitHub 清单", () => { _ = LoadGithubListAsync(); });
        b.CloseElement();

        if (_storeInfo != null)
        {
            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style",
                "border:1px solid #e3e3e3;border-radius:10px;background:#fff;margin-top:8px;padding:12px 14px;");

            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style", "font-size:14px;font-weight:700;");
            string storeTitle = string.IsNullOrWhiteSpace(_storeInfo.ChineseName)
                ? _storeInfo.Name
                : _storeInfo.ChineseName;
            b.AddContent(i++, storeTitle);
            b.CloseElement();

            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style",
                "margin-top:2px;font-size:12px;color:#888;word-break:break-all;");
            string typeLabel = _storeInfo.Hosted ? "Hosted（托管）" : "Local（本地命令）";
            b.AddContent(i++, $"{_storeInfo.Path}/{_storeInfo.Name} · {typeLabel} · {_storeInfo.ToolCount} 个工具");
            b.CloseElement();

            if (!string.IsNullOrWhiteSpace(_storeInfo.Description))
            {
                b.OpenElement(i++, "div");
                b.AddAttribute(i++, "style",
                    "margin-top:6px;font-size:12px;color:#555;line-height:1.6;");
                b.AddContent(i++, _storeInfo.Description);
                b.CloseElement();
            }

            if (!string.IsNullOrWhiteSpace(_storeInfo.Url))
            {
                b.OpenElement(i++, "a");
                b.AddAttribute(i++, "href", _storeInfo.Url);
                b.AddAttribute(i++, "target", "_blank");
                b.AddAttribute(i++, "rel", "noopener");
                b.AddAttribute(i++, "style",
                    "margin-top:6px;display:inline-block;font-size:12px;color:#1677ff;text-decoration:none;word-break:break-all;");
                b.AddContent(i++, _storeInfo.Url);
                b.CloseElement();
            }

            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style", "margin-top:10px;");
            AddButton(b, ref i, "安装到 McpService", InstallStoreServer);
            b.CloseElement();

            b.CloseElement();
        }

        if (_marketplaceList != null)
        {
            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style", "margin-top:8px;");

            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style", "font-size:12px;font-weight:600;margin-bottom:4px;color:#333;");
            b.AddContent(i++, $"魔搭 MCP 广场（{_marketplaceList.Count} 个，最多显示 200 个）");
            b.CloseElement();

            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style", "display:flex;gap:6px;align-items:center;margin-bottom:6px;");
            b.OpenElement(i++, "input");
            b.AddAttribute(i++, "type", "text");
            b.AddAttribute(i++, "value", _marketplaceSearch);
            b.AddAttribute(i++, "placeholder", "搜索广场（如 fetch / 高德），回车或点搜索");
            b.AddAttribute(i++, "style",
                "flex:1;min-width:0;box-sizing:border-box;padding:6px 9px;border:1px solid #d9d9d9;border-radius:6px;font-size:12px;");
            b.AddAttribute(i++, "onchange",
                EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
                {
                    if (e.Value is string s)
                        _marketplaceSearch = s;
                }));
            b.AddAttribute(i++, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, e =>
                {
                    if (e.Key == "Enter")
                        _ = SearchMarketplaceAsync();
                }));
            b.CloseElement();
            AddButton(b, ref i, "搜索", () => { _ = SearchMarketplaceAsync(); });
            b.CloseElement();

            if (_marketplaceList.Count == 0)
            {
                Hint(b, ref i, "没有找到匹配的 MCP 服务，换个关键字试试。");
            }
            else
            {
                int shown = 0;
                foreach (McpServerInfo server in _marketplaceList)
                {
                    if (shown >= 200)
                        break;

                    McpServerInfo s = server;
                    b.OpenElement(i++, "div");
                    b.AddAttribute(i++, "style",
                        "border:1px solid #e3e3e3;border-radius:8px;margin-bottom:6px;background:#fff;padding:8px 10px;");
                    b.OpenElement(i++, "div");
                    b.AddAttribute(i++, "style",
                        "display:flex;align-items:center;justify-content:space-between;gap:8px;");
                    b.OpenElement(i++, "span");
                    b.AddAttribute(i++, "style", "font-size:13px;font-weight:600;word-break:break-all;");
                    string itemTitle = string.IsNullOrWhiteSpace(s.ChineseName) ? s.Name : s.ChineseName;
                    b.AddContent(i++, $"{itemTitle} · {s.Path}/{s.Name}");
                    b.CloseElement();
                    AddButton(b, ref i, "安装", () => { _ = InstallMarketplaceItemAsync(s); });
                    b.CloseElement();
                    if (!string.IsNullOrWhiteSpace(s.Description))
                    {
                        b.OpenElement(i++, "div");
                        b.AddAttribute(i++, "style", "margin-top:4px;font-size:12px;color:#666;line-height:1.5;");
                        b.AddContent(i++, s.Description);
                        b.CloseElement();
                    }
                    b.CloseElement();
                    shown++;
                }
            }

            if (_marketplaceList.Count > 0)
            {
                b.OpenElement(i++, "div");
                b.AddAttribute(i++, "style", "margin-top:6px;");
                AddButton(b, ref i, "加载更多（第 " + (_marketplacePage + 1) + " 页）", () => { _ = LoadMarketplaceMoreAsync(); });
                b.CloseElement();
            }

            b.CloseElement();
        }

        if (_githubList != null && _githubList.Count > 0)
        {
            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style", "margin-top:8px;");

            b.OpenElement(i++, "div");
            b.AddAttribute(i++, "style", "font-size:12px;font-weight:600;margin-bottom:4px;color:#333;");
            b.AddContent(i++, $"GitHub MCP 清单（共 {_githubList.Count} 个，输入关键字过滤，最多显示 200 个）");
            b.CloseElement();

            b.OpenElement(i++, "input");
            b.AddAttribute(i++, "type", "text");
            b.AddAttribute(i++, "value", _githubSearch);
            b.AddAttribute(i++, "placeholder", "搜索关键字（如 pdf / search / github）");
            b.AddAttribute(i++, "style",
                "width:100%;box-sizing:border-box;padding:6px 9px;border:1px solid #d9d9d9;border-radius:6px;font-size:12px;margin-bottom:6px;");
            b.AddAttribute(i++, "onchange",
                EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
                {
                    if (e.Value is string s)
                        _githubSearch = s;
                }));
            b.CloseElement();

            int shown = 0;
            foreach (McpServerInfo server in _githubList)
            {
                string hay = (server.ChineseName + " " + server.Name + " " + server.Path + " " + server.Description)
                    .ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(_githubSearch) == false &&
                    hay.Contains(_githubSearch.Trim().ToLowerInvariant()) == false)
                {
                    continue;
                }
                if (shown >= 200)
                    break;

                McpServerInfo s = server;
                b.OpenElement(i++, "div");
                b.AddAttribute(i++, "style",
                    "border:1px solid #e3e3e3;border-radius:8px;margin-bottom:6px;background:#fff;padding:8px 10px;");
                b.OpenElement(i++, "div");
                b.AddAttribute(i++, "style",
                    "display:flex;align-items:center;justify-content:space-between;gap:8px;");
                b.OpenElement(i++, "span");
                b.AddAttribute(i++, "style", "font-size:13px;font-weight:600;word-break:break-all;");
                b.AddContent(i++, s.ChineseName);
                b.CloseElement();
                AddButton(b, ref i, "安装", () => { _ = InstallGithubItemAsync(s); });
                b.CloseElement();
                if (!string.IsNullOrWhiteSpace(s.Description))
                {
                    b.OpenElement(i++, "div");
                    b.AddAttribute(i++, "style", "margin-top:4px;font-size:12px;color:#666;line-height:1.5;");
                    b.AddContent(i++, s.Description);
                    b.CloseElement();
                }
                b.CloseElement();
                shown++;
            }

            b.CloseElement();
        }

        if (!string.IsNullOrEmpty(_storeStatus))
            Hint(b, ref i, _storeStatus);

        SectionTitle(b, ref i, "已安装（Alife.Function.Mcp.McpService）");
        Hint(b, ref i,
            "安装后在这里看到列表。Hosted 服务需到官方 MCP 插件里填魔搭 Remote URL（服务详情页点「连接」获取）；本地命令服务已预填命令。重载后由官方插件连接。");

        List<McpInstalledInfo> installed = McpModule.ListInstalledFromMcpService();
        if (installed.Count == 0)
        {
            Hint(b, ref i, "还没有安装任何 MCP 服务。");
        }
        else
        {
            foreach (McpInstalledInfo server in installed)
            {
                string name = server.Name;
                b.OpenElement(i++, "div");
                b.AddAttribute(i++, "style",
                    "display:flex;align-items:center;justify-content:space-between;gap:8px;padding:8px 10px;border:1px solid #e3e3e3;border-radius:8px;margin-bottom:6px;background:#fff;");
                b.OpenElement(i++, "span");
                b.AddAttribute(i++, "style", "font-size:12px;color:#333;word-break:break-all;");
                b.AddContent(i++, $"{server.Name} · {(server.Enabled ? "启用" : "停用")} · {server.Type} · {server.Address}");
                b.CloseElement();
                DangerButton(b, ref i, "删除", () =>
                {
                    McpModule.RemoveFromMcpService(name);
                    _ = InvokeAsync(StateHasChanged);
                });
                b.CloseElement();
            }
        }

        b.CloseElement();
    }

    private async Task LoadStoreServerAsync()
    {
        if (_storeLoading)
            return;

        if (IsMarketplaceUrl(_storeInput))
        {
            await SearchMarketplaceAsync();
            return;
        }

        if (!TryParseServerKey(_storeInput, out string owner, out string name))
        {
            _storeStatus = "格式不对：请输入魔搭 MCP 服务详情页 URL 或 owner/name（如 https://modelscope.cn/mcp/servers/@modelcontextprotocol/fetch）。";
            _storeInfo = null;
            _marketplaceList = null;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _storeLoading = true;
        _storeStatus = "加载中…";
        _storeInfo = null;
        _marketplaceList = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            _storeInfo = await McpModule.FetchMcpServerDetailAsync(owner, name);
            _storeStatus = "";
        }
        catch (Exception ex)
        {
            _storeStatus = $"加载失败：{ex.Message}";
        }
        finally
        {
            _storeLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private static bool IsMarketplaceUrl(string input)
    {
        string s = (input ?? "").Trim().TrimEnd('/');
        return s.Equals("https://modelscope.cn/mcp", StringComparison.OrdinalIgnoreCase) ||
               s.Equals("https://www.modelscope.cn/mcp", StringComparison.OrdinalIgnoreCase) ||
               s.Equals("modelscope:mcp", StringComparison.OrdinalIgnoreCase) ||
               s.Equals("ms:mcp", StringComparison.OrdinalIgnoreCase);
    }

    private void InstallStoreServer()
    {
        if (_storeInfo == null)
            return;

        try
        {
            McpModule.InstallToMcpService(_storeInfo);
            string shown = string.IsNullOrWhiteSpace(_storeInfo.ChineseName)
                ? _storeInfo.Name
                : _storeInfo.ChineseName;
            _storeStatus = $"已安装「{shown}」到 Alife.Function.Mcp.McpService，重载后生效。";
        }
        catch (Exception ex)
        {
            _storeStatus = $"安装失败：{ex.Message}";
        }
        _ = InvokeAsync(StateHasChanged);
    }

    private async Task InstallMarketplaceItemAsync(McpServerInfo info)
    {
        try
        {
            McpServerInfo detail = await McpModule.FetchMcpServerDetailAsync(info.Path, info.Name);
            McpModule.InstallToMcpService(detail);
            string shown = string.IsNullOrWhiteSpace(detail.ChineseName) ? detail.Name : detail.ChineseName;
            _storeStatus = $"已安装「{shown}」到 Alife.Function.Mcp.McpService，重载后生效。";
        }
        catch (Exception ex)
        {
            _storeStatus = $"安装失败：{ex.Message}";
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task SearchMarketplaceAsync()
    {
        if (_storeLoading)
            return;

        string keyword = _marketplaceSearch.Trim();
        _storeLoading = true;
        _storeStatus = keyword.Length == 0
            ? "正在用内置浏览器打开魔搭 MCP 广场并读取列表…"
            : $"正在搜索「{keyword}」…";
        _storeInfo = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            _marketplaceList = await McpModule.FetchMarketplaceMcpServersPageAsync(keyword, 1);
            _marketplacePage = 1;
            _storeStatus = _marketplaceList.Count == 0
                ? "没有找到匹配的 MCP 服务，换个关键字试试。"
                : $"第 1 页找到 {_marketplaceList.Count} 个 MCP 服务，点「安装」加入 McpService，或点「加载更多」继续。";
        }
        catch (Exception ex)
        {
            _storeStatus = $"搜索失败：{ex.Message}";
        }
        finally
        {
            _storeLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadMarketplaceMoreAsync()
    {
        if (_storeLoading)
            return;

        int next = _marketplacePage + 1;
        string keyword = _marketplaceSearch.Trim();
        _storeLoading = true;
        _storeStatus = $"正在加载第 {next} 页…";
        await InvokeAsync(StateHasChanged);

        try
        {
            List<McpServerInfo> more = await McpModule.FetchMarketplaceMcpServersPageAsync(keyword, next);
            _marketplaceList ??= new List<McpServerInfo>();
            foreach (McpServerInfo info in more)
            {
                string key = $"{info.Path}/{info.Name}";
                if (_marketplaceList.Any(x => string.Equals($"{x.Path}/{x.Name}", key, StringComparison.OrdinalIgnoreCase)) == false)
                    _marketplaceList.Add(info);
            }
            _marketplacePage = next;
            _storeStatus = $"已加载到第 {_marketplacePage} 页，共 {_marketplaceList.Count} 个。";
        }
        catch (Exception ex)
        {
            _storeStatus = $"加载更多失败：{ex.Message}";
        }
        finally
        {
            _storeLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadGithubListAsync()
    {
        if (_storeLoading)
            return;

        _storeLoading = true;
        _storeStatus = "正在获取 GitHub MCP 清单…";
        _storeInfo = null;
        _marketplaceList = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            _githubList = await McpModule.FetchGithubMcpListAsync();
            _storeStatus = $"GitHub 清单共 {_githubList.Count} 个 MCP 服务器。安装后请在官方 MCP 插件里补全启动命令（见各仓库说明）。";
        }
        catch (Exception ex)
        {
            _storeStatus = $"获取 GitHub 清单失败：{ex.Message}";
        }
        finally
        {
            _storeLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task InstallGithubItemAsync(McpServerInfo info)
    {
        try
        {
            McpModule.InstallToMcpService(info);
            string shown = string.IsNullOrWhiteSpace(info.ChineseName) ? info.Name : info.ChineseName;
            _storeStatus = $"已添加「{shown}」到 Alife.Function.Mcp.McpService。启动命令为空，需在官方 MCP 插件里补全（见仓库 {info.Url} 的说明）。";
        }
        catch (Exception ex)
        {
            _storeStatus = $"安装失败：{ex.Message}";
        }
        await InvokeAsync(StateHasChanged);
    }

    private static bool TryParseServerKey(string input, out string owner, out string name)
    {
        owner = "";
        name = "";

        string s = (input ?? "").Trim();
        if (s.Length == 0)
            return false;

        int idx = s.IndexOf("/mcp/servers/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            s = s.Substring(idx + "/mcp/servers/".Length);
        s = s.Trim().TrimEnd('/');

        int slash = s.IndexOf('/');
        if (slash <= 0 || slash == s.Length - 1)
            return false;

        owner = s.Substring(0, slash).Trim();
        name = s.Substring(slash + 1).Trim();
        return owner.Length > 0 && name.Length > 0;
    }

    // ── 基础元素 ──

    void AddButton(RenderTreeBuilder b, ref int seq, string text, Action onClick)
    {
        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "type", "button");
        b.AddAttribute(seq++, "style",
            "margin-top:6px;padding:6px 12px;border:1px dashed #1677ff;border-radius:6px;background:#fff;color:#1677ff;cursor:pointer;font-size:12px;");
        b.AddAttribute(seq++, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, _ => { onClick(); InvokeAsync(StateHasChanged); }));
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void DangerButton(RenderTreeBuilder b, ref int seq, string text, Action onClick)
    {
        b.OpenElement(seq++, "button");
        b.AddAttribute(seq++, "type", "button");
        b.AddAttribute(seq++, "style",
            "padding:5px 11px;border:1px solid #ffa39e;border-radius:6px;background:#fff;color:#ff4d4f;cursor:pointer;font-size:12px;");
        b.AddAttribute(seq++, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, _ => { onClick(); InvokeAsync(StateHasChanged); }));
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    static void SectionTitle(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style",
            "font-size:14px;font-weight:700;color:#444;margin:16px 0 6px;border-bottom:1px solid #eee;padding-bottom:6px;");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    static void Hint(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "font-size:12px;color:#999;margin:2px 0 6px;line-height:1.6;");
        b.AddContent(seq++, text);
        b.CloseElement();
    }
}
