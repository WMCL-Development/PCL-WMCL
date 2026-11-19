namespace PCL.UI;

/// <summary>
///     界面缩放变更通知：桌面由主窗口直接响应，Android 没有主窗口，
///     由外壳订阅此事件重新应用缩放。
/// </summary>
public static class UiScaleNotifier
{
    public static event Action? Changed;

    public static void Notify() => Changed?.Invoke();
}
