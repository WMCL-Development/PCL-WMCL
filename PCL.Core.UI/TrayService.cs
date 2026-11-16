using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Logging;

namespace PCL.Core.UI;

/// <summary>
///     系统托盘：最小化/关闭到托盘后仍能从托盘唤回窗口。
///     托盘在部分 Linux 桌面上不可用（没有 StatusNotifier 宿主），此时静默降级。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Window _window;
    private TrayIcon? _icon;

    private TrayService(Window window) => _window = window;

    /// <summary>托盘是否可用（创建失败时为 false，调用方应退回直接关闭）。</summary>
    public bool IsAvailable => _icon is not null;

    /// <summary>挂到指定窗口；失败时返回不可用的实例，不抛异常。</summary>
    public static TrayService Attach(Window window)
    {
        var service = new TrayService(window);
        try
        {
            service.Create();
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("UI", "创建托盘失败，已退回普通窗口行为：" + exception.Message);
        }
        return service;
    }

    private void Create()
    {
        using var stream = AssetLoader.Open(new Uri("avares://PCL.Launcher/Assets/Images/icon.ico"));
        var icon = new WindowIcon(stream);

        var menu = new NativeMenu();
        var show = new NativeMenuItem(Lang.Text("Tray.ShowWindow"));
        show.Click += (_, _) => ShowWindow();
        var exit = new NativeMenuItem(Lang.Text("Tray.Exit"));
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(show);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        _icon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = Lang.Text("Tray.ToolTip"),
            Menu = menu,
            IsVisible = true
        };
        _icon.Clicked += (_, _) => ShowWindow();

        if (Application.Current is { } app)
        {
            // Avalonia 12 用附加属性挂托盘：TrayIcon.GetIcons / SetIcons。
            var icons = TrayIcon.GetIcons(app) ?? new TrayIcons();
            icons.Add(_icon);
            TrayIcon.SetIcons(app, icons);
        }
    }

    /// <summary>从托盘唤回窗口。</summary>
    public void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApplication()
    {
        Config.Preference.CloseToTray = false;
        _window.Close();
        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public void Dispose()
    {
        if (_icon is null) return;
        if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons)
            icons.Remove(_icon);
        _icon.Dispose();
        _icon = null;
    }
}
