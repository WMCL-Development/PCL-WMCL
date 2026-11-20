using Android.Views;

namespace PCL.Android;

// The launcher owns its SurfaceView and forwards only the Surface over Binder.
internal sealed class AndroidLwjglWindow(Surface surface, Action<int, int> rendered)
{
    public Surface Surface { get; } = surface;
    public event Action<int, int, bool>? Touched;
    public event Action<int, int>? Resized;

    public void PushTouch(int x, int y, bool pressed) => Touched?.Invoke(x, y, pressed);
    public void Resize(int width, int height) => Resized?.Invoke(width, height);
    public void FramePresented(int frames, int touches) => rendered(frames, touches);
}
