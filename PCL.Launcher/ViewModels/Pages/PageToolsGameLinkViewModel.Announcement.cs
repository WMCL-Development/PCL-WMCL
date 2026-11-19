using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net.Http;
using PCL.Core.Link;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.McPing;
using PCL.Core.Link.Natayark;
using PCL.Core.Link.Scaffolding.Client.Models;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;
using PCL.Core.Minecraft.Profile;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Core.Utils;
using PCL.Core.Utils.Validate;
using PCL.Core.ViewModel;

namespace PCL;

public partial class PageToolsGameLinkViewModel
{

    private void BeginAnnouncementReload()
    {
        var requestId = BeginAnnouncementRequest();
        Suspend();

        _announcementCarouselCts = new CancellationTokenSource();
        _announcementIndex = 0;
        Volatile.Write(ref _announcementItems, Array.Empty<LinkAnnounceInfo>());
        SetAnnouncementState(AnnouncementLoadState.Loading, AnnouncementFailureKind.None);
        AnnouncementThemeIndex = 0;
        AnnouncementText = Lang.Text("Tools.GameLink.Loading.ConnectingServer");
        AnnouncementVisible = true;

        _ = SafeFetchAnnouncementAsync(requestId);
        _ = RunAnnouncementCarouselAsync(_announcementCarouselCts.Token);
    }

    private int BeginAnnouncementRequest()
    {
        lock (_announcementLock)
        {
            return ++_announcementRequestId;
        }
    }

    private bool IsCurrentAnnouncementRequest(int requestId)
    {
        lock (_announcementLock)
        {
            return requestId == _announcementRequestId;
        }
    }

    private bool TryApplyCurrentAnnouncementRequest(int requestId, Action action)
    {
        lock (_announcementLock)
        {
            if (requestId != _announcementRequestId)
                return false;
            action();
            return true;
        }
    }

    private AnnouncementLoadState CurrentAnnouncementState =>
        (AnnouncementLoadState)Volatile.Read(ref _announcementState);

    private AnnouncementFailureKind CurrentAnnouncementFailure =>
        (AnnouncementFailureKind)Volatile.Read(ref _announcementFailureKind);

    private void SetAnnouncementState(
        AnnouncementLoadState state,
        AnnouncementFailureKind failureKind)
    {
        Volatile.Write(ref _announcementState, (int)state);
        Volatile.Write(ref _announcementFailureKind, (int)failureKind);
    }

    private async Task RunAnnouncementCarouselAsync(CancellationToken token)
    {
        var index = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(AnnouncementIntervalMilliseconds, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    break;

                if (CurrentAnnouncementState != AnnouncementLoadState.Loaded)
                    continue;

                var items = Volatile.Read(ref _announcementItems);
                if (items.Length == 0)
                {
                    RunOnUi(() =>
                    {
                        if (CurrentAnnouncementState == AnnouncementLoadState.Loaded)
                            AnnouncementVisible = false;
                    });
                    continue;
                }

                index = (index + 1) % items.Length;
                var next = index;
                RunOnUi(() =>
                {
                    if (CurrentAnnouncementState != AnnouncementLoadState.Loaded)
                        return;
                    ShowAnnouncement(next);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 页面刷新或离开时正常结束轮播。
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Link", "联机大厅公告轮播失败");
        }
    }

    private void ShowAnnouncement(int index)
    {
        var items = Volatile.Read(ref _announcementItems);
        if (items.Length == 0)
        {
            AnnouncementVisible = false;
            return;
        }

        _announcementIndex = Math.Abs(index) % items.Length;
        var info = items[_announcementIndex];
        var (prefix, theme) = info.Type switch
        {
            LinkAnnounceType.Important => (
                Lang.Text("Tools.GameLink.Announcement.Important"), 1),
            LinkAnnounceType.Warning => (
                Lang.Text("Tools.GameLink.Announcement.Warning"), 2),
            _ => (
                Lang.Text("Tools.GameLink.Announcement.Notice"), 0)
        };

        AnnouncementThemeIndex = theme;
        AnnouncementText = Lang.Text(
            "Tools.GameLink.Announcement.Format",
            prefix,
            info.Content.Replace("\r\n", "\n").Replace("\r", "\n"));
        AnnouncementVisible = true;
    }

    private void RefreshAnnouncementText()
    {
        switch (CurrentAnnouncementState)
        {
            case AnnouncementLoadState.Loading:
                AnnouncementThemeIndex = 0;
                AnnouncementText = Lang.Text("Tools.GameLink.Loading.ConnectingServer");
                AnnouncementVisible = true;
                break;
            case AnnouncementLoadState.Loaded:
                if (Volatile.Read(ref _announcementItems).Length == 0)
                    AnnouncementVisible = false;
                else
                    ShowAnnouncement(_announcementIndex);
                break;
            case AnnouncementLoadState.Failed:
                AnnouncementThemeIndex = 1;
                AnnouncementText =
                    CurrentAnnouncementFailure == AnnouncementFailureKind.UpdateRequired
                        ? Lang.Text("Tools.GameLink.Error.UpdateRequired")
                        : Lang.Text("Tools.GameLink.Error.ConnectFailed");
                AnnouncementVisible = true;
                break;
        }
    }

    /// <summary>
    ///     公告加载是后台发起、不等待的任务，这里兜住所有异常，
    ///     避免未观察异常冒到全局处理器弹出错误框。
    /// </summary>
    private async Task SafeFetchAnnouncementAsync(int requestId)
    {
        try
        {
            await FetchAnnouncementAsync(requestId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWrapper.Debug("Link", "联机大厅公告加载异常已忽略：" + ex.Message);
        }
    }

    private async Task FetchAnnouncementAsync(int requestId)
    {
        try
        {
            var root = await RequestAnnouncementAsync(requestId).ConfigureAwait(false);
            if (!IsCurrentAnnouncementRequest(requestId))
                return;

            if (root["available"] is null)
                throw new InvalidDataException("联机大厅公告缺少可用状态");

            var available = ReadBool(root["available"], false);
            var allowCustomName = ReadBool(root["allowCustomName"], false);
            var requiresLogin = ReadBool(root["requireLogin"], true);
            var requiresRealName = ReadBool(root["requireRealname"], true);
            var protocolVersion = ReadDouble(root["version"], double.NaN);
            if (double.IsNaN(protocolVersion))
                throw new InvalidDataException("联机大厅公告缺少协议版本号");

            if (protocolVersion > LobbyInfoProvider.ProtocolVersion)
            {
                LobbyInfoProvider.IsLobbyAvailable = false;
                SetAnnouncementState(
                    AnnouncementLoadState.Failed,
                    AnnouncementFailureKind.UpdateRequired);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsCurrentAnnouncementRequest(requestId))
                        return;
                    AnnouncementThemeIndex = 1;
                    AnnouncementText = Lang.Text("Tools.GameLink.Error.UpdateRequired");
                    AnnouncementVisible = true;
                });
                return;
            }

            var announcements = ParseAnnouncements(root);
            var relays = ParseRelays(root);

            if (!IsCurrentAnnouncementRequest(requestId))
                return;

            LobbyInfoProvider.IsLobbyAvailable = available;
            LobbyInfoProvider.AllowCustomName = allowCustomName;
            LobbyInfoProvider.RequiresLogin = requiresLogin;
            LobbyInfoProvider.RequiresRealName = requiresRealName;
            ETRelay.RelayList = relays;

            Volatile.Write(ref _announcementItems, announcements.ToArray());
            SetAnnouncementState(AnnouncementLoadState.Loaded, AnnouncementFailureKind.None);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentAnnouncementRequest(requestId))
                    return;
                _announcementIndex = 0;
                if (announcements.Count == 0)
                    AnnouncementVisible = false;
                else
                    ShowAnnouncement(0);
                EnsureAccountState();
            });
        }
        catch (OperationCanceledException)
        {
            // 请求已被新的 Reload 作废，不再覆盖当前界面。
        }
        catch (Exception ex)
        {
            LogWrapper.Debug("Link", "获取联机大厅公告失败：" + ex.Message);
            if (!IsCurrentAnnouncementRequest(requestId))
                return;

            LobbyInfoProvider.IsLobbyAvailable = false;
            SetAnnouncementState(AnnouncementLoadState.Failed, AnnouncementFailureKind.Connect);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentAnnouncementRequest(requestId))
                    return;
                AnnouncementThemeIndex = 1;
                AnnouncementText = Lang.Text("Tools.GameLink.Error.ConnectFailed");
                AnnouncementVisible = true;
            });
        }
    }

    private async Task<JsonObject> RequestAnnouncementAsync(int requestId)
    {
        JsonObject? result = null;
        Exception? lastError = null;

        foreach (var rawServer in Secrets.LinkServers)
        {
            var server = rawServer.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(server))
                continue;

            try
            {
                var cacheVersionText = await FetchStringAsync($"{server}/api/link/v2/cache.ini")
                    .ConfigureAwait(false);
                if (!int.TryParse(
                        cacheVersionText.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var cacheVersion))
                    throw new InvalidDataException("联机大厅缓存版本号无效");

                if (cacheVersion == States.Link.AnnounceCacheVer
                    && !string.IsNullOrWhiteSpace(States.Link.AnnounceCache))
                {
                    result = ParseAnnouncementRoot(States.Link.AnnounceCache);
                }
                else
                {
                    var received = await FetchStringAsync($"{server}/api/link/v2/announce.json")
                        .ConfigureAwait(false);
                    var parsed = ParseAnnouncementRoot(received);
                    if (!TryApplyCurrentAnnouncementRequest(
                            requestId,
                            () =>
                            {
                                States.Link.AnnounceCache = received;
                                States.Link.AnnounceCacheVer = cacheVersion;
                            }))
                        throw new OperationCanceledException();

                    result = parsed;
                }

                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                LogWrapper.Debug("Link", $"从服务器 {server} 获取联机大厅公告失败：{ex.Message}");
                if (!TryApplyCurrentAnnouncementRequest(
                        requestId,
                        () =>
                        {
                            States.Link.AnnounceCache = string.Empty;
                            States.Link.AnnounceCacheVer = 0;
                        }))
                    throw new OperationCanceledException();
            }
        }

        if (result is not null)
            return result;

        // 公告是非必需内容：全部服务器不可用时返回空对象，由调用方按「连接失败」降级处理，
        // 不要把异常抛到界面层弹错误框。
        LogWrapper.Debug("Link", "所有联机大厅服务器均不可用，公告功能暂时关闭");
        return new JsonObject();
    }

    private static async Task<string> FetchStringAsync(string url)
    {
        using var cts = new CancellationTokenSource(AnnouncementRequestTimeoutMilliseconds);
        using var response = await HttpRequest
            .Create(url)
            .SendAsync(cancellationToken: cts.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.AsStringAsync(cts.Token).ConfigureAwait(false);
    }

    private static JsonObject ParseAnnouncementRoot(string text)
    {
        return JsonCompat.ParseNode(text) as JsonObject
            ?? throw new InvalidDataException("联机大厅公告格式无效");
    }

    private static List<LinkAnnounceInfo> ParseAnnouncements(JsonObject root)
    {
        if (root["notices"] is not JsonArray notices)
            throw new InvalidDataException("联机大厅公告列表格式无效");

        var result = new List<LinkAnnounceInfo>();
        foreach (var noticeNode in notices)
        {
            if (noticeNode is not JsonObject notice)
                continue;

            var content = notice["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var minVersion = ReadDouble(notice["minVer"], 0d);
            var maxVersion = ReadDouble(notice["maxVer"], double.MaxValue);
            if (Basics.VersionCode < minVersion || Basics.VersionCode > maxVersion)
                continue;

            var typeText = notice["type"]?.ToString().ToLowerInvariant();
            var type = typeText switch
            {
                "important" or "red" => LinkAnnounceType.Important,
                "warning" or "yellow" => LinkAnnounceType.Warning,
                _ => LinkAnnounceType.Notice
            };

            foreach (var line in content.Split(
                         '\n',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result.Add(new LinkAnnounceInfo(type, line));
        }

        return result;
    }

    private static List<ETRelay> ParseRelays(JsonObject root)
    {
        if (root["relays"] is not JsonArray relays)
            throw new InvalidDataException("联机大厅中继列表格式无效");

        var result = new List<ETRelay>();
        foreach (var relayNode in relays)
        {
            if (relayNode is not JsonObject relay)
                continue;

            var name = relay["name"]?.ToString();
            var url = relay["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            result.Add(
                new ETRelay
                {
                    Name = name,
                    Url = url,
                    Type = string.Equals(
                        relay["type"]?.ToString(),
                        "official",
                        StringComparison.OrdinalIgnoreCase)
                        ? ETRelayType.Selfhosted
                        : ETRelayType.Community
                });
        }

        return result;
    }

    private static bool ReadBool(JsonNode? node, bool fallback)
    {
        if (node is null)
            return fallback;

        try
        {
            return node.GetValue<bool>();
        }
        catch (Exception)
        {
            return bool.TryParse(node.ToString(), out var value) ? value : fallback;
        }
    }

    private static double ReadDouble(JsonNode? node, double fallback)
    {
        if (node is null)
            return fallback;

        return double.TryParse(
            node.ToString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;
    }
}
