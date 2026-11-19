using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using PCL.Platform.Android;

namespace PCL.Android;

/// <summary>
///     系统 SAF 文件选择/保存的 Activity 侧实现。
///     PCL.Platform.Android 拿不到 onActivityResult，所以由这里接管 Intent 并桥接给平台服务。
/// </summary>
internal static class AndroidFilePicker
{
    private const int RequestPick = 0x9A11;
    private const int RequestSave = 0x9A12;
    private const int RequestFolder = 0x9A13;

    private static Activity? _activity;
    private static TaskCompletionSource<string[]>? _pickTcs;
    private static TaskCompletionSource<string>? _saveTcs;
    private static TaskCompletionSource<string>? _folderTcs;

    public static void Initialize(Activity activity)
    {
        _activity = activity;
        AndroidFilePickerBridge.PickFiles = PickFilesAsync;
        AndroidFilePickerBridge.SaveFile = SaveFileAsync;
        AndroidFilePickerBridge.PickFolder = PickFolderAsync;
        AndroidFilePickerBridge.IsMainThread = () =>
            Looper.MyLooper() == Looper.MainLooper;
    }

    private static Task<string[]> PickFilesAsync(bool allowMultiple)
    {
        if (_activity is null)
            return Task.FromResult(Array.Empty<string>());

        _pickTcs?.TrySetResult(Array.Empty<string>());
        _pickTcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            intent.PutExtra(Intent.ExtraAllowMultiple, allowMultiple);
            var activity = _activity;
            activity.RunOnUiThread(() =>
            {
                try { activity.StartActivityForResult(intent, RequestPick); }
                catch (Exception) { _pickTcs?.TrySetResult(Array.Empty<string>()); }
            });
        }
        catch (Exception)
        {
            _pickTcs.TrySetResult(Array.Empty<string>());
        }

        return _pickTcs.Task;
    }

    private static Task<string> SaveFileAsync(string fileName)
    {
        if (_activity is null)
            return Task.FromResult("");

        _saveTcs?.TrySetResult("");
        _saveTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            intent.PutExtra(Intent.ExtraTitle, fileName);
            var activity = _activity;
            activity.RunOnUiThread(() =>
            {
                try { activity.StartActivityForResult(intent, RequestSave); }
                catch (Exception) { _saveTcs?.TrySetResult(""); }
            });
        }
        catch (Exception)
        {
            _saveTcs.TrySetResult("");
        }

        return _saveTcs.Task;
    }

    private static Task<string> PickFolderAsync()
    {
        if (_activity is null)
            return Task.FromResult("");

        _folderTcs?.TrySetResult("");
        _folderTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission
                            | ActivityFlags.GrantPersistableUriPermission
                            | ActivityFlags.GrantPrefixUriPermission);
            var activity = _activity;
            activity.RunOnUiThread(() =>
            {
                try { activity.StartActivityForResult(intent, RequestFolder); }
                catch (Exception) { _folderTcs?.TrySetResult(""); }
            });
        }
        catch (Exception)
        {
            _folderTcs.TrySetResult("");
        }

        return _folderTcs.Task;
    }

    /// <summary>由 MainActivity.OnActivityResult 调用；返回是否已消费该请求。</summary>
    public static bool OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        switch (requestCode)
        {
            case RequestPick:
            {
                var tcs = _pickTcs;
                _pickTcs = null;
                if (tcs is null) return true;

                _ = Task.Run(() =>
                {
                    var paths = new List<string>();
                    try
                    {
                        if (resultCode == Result.Ok && data is not null)
                        {
                            if (data.Data is { } single)
                                paths.Add(CopyToCache(single));
                            if (data.ClipData is { } clip)
                                for (var i = 0; i < clip.ItemCount; i++)
                                    if (clip.GetItemAt(i)?.Uri is { } uri)
                                        paths.Add(CopyToCache(uri));
                        }
                    }
                    catch (Exception)
                    {
                        // 选择或复制失败时返回已收集到的结果
                    }

                    tcs.TrySetResult(paths.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray());
                });
                return true;
            }
            case RequestSave:
            {
                var tcs = _saveTcs;
                _saveTcs = null;
                if (tcs is null) return true;

                if (resultCode != Result.Ok || data?.Data is not { } uri)
                {
                    tcs.TrySetResult("");
                    return true;
                }

                // 返回一个临时文件路径给调用方写入，写完后由后台任务复制到用户选中的位置。
                var tempDir = Path.Combine(_activity?.CacheDir?.AbsolutePath ?? Path.GetTempPath(), "saf-save");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".bin");
                tcs.TrySetResult(tempPath);
                _ = CopyBackWhenWrittenAsync(tempPath, uri);
                return true;
            }
            case RequestFolder:
            {
                var tcs = _folderTcs;
                _folderTcs = null;
                if (tcs is null) return true;

                if (resultCode != Result.Ok || data?.Data is not { } treeUri)
                {
                    tcs.TrySetResult("");
                    return true;
                }

                try
                {
                    _activity?.ContentResolver?.TakePersistableUriPermission(
                        treeUri,
                        ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
                }
                catch (Exception)
                {
                    // 权限保留失败不影响本次使用
                }

                tcs.TrySetResult(TreeUriToPath(treeUri));
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    ///     把 SAF 的目录 URI 映射成本机路径；仅主存储与可识别的卷能映射，其余返回空字符串。
    /// </summary>
    private static string TreeUriToPath(global::Android.Net.Uri treeUri)
    {
        try
        {
            var docId = DocumentsContract.GetTreeDocumentId(treeUri);
            if (string.IsNullOrEmpty(docId)) return "";

            var parts = docId.Split(':', 2);
            if (parts.Length != 2) return "";

            var volume = parts[0];
            var relative = parts[1].Replace('/', System.IO.Path.DirectorySeparatorChar);
            var root = volume.Equals("primary", StringComparison.OrdinalIgnoreCase)
                ? global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
                : "/storage/" + volume;

            if (string.IsNullOrEmpty(root)) return "";
            return string.IsNullOrEmpty(relative)
                ? root
                : System.IO.Path.Combine(root, relative);
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static async Task CopyBackWhenWrittenAsync(string tempPath, global::Android.Net.Uri uri)
    {
        try
        {
            var resolver = _activity?.ContentResolver;
            if (resolver is null) return;

            for (var i = 0; i < 150; i++)
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (!File.Exists(tempPath)) continue;

                try
                {
                    await using var input = File.OpenRead(tempPath);
                    await using var output = resolver.OpenOutputStream(uri)
                        ?? throw new IOException("无法打开目标文件");
                    await input.CopyToAsync(output).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 目标被占用等情况忽略
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { /* ignore */ }
                }

                return;
            }
        }
        catch (Exception)
        {
            // ignore
        }
    }

    private static string CopyToCache(global::Android.Net.Uri uri)
    {
        var resolver = _activity?.ContentResolver;
        if (resolver is null) return "";

        var name = QueryDisplayName(resolver, uri) ?? "picked";
        var ext = Path.GetExtension(name);
        var dir = Path.Combine(_activity!.CacheDir!.AbsolutePath, "saf-pick");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, Guid.NewGuid().ToString("N") + (string.IsNullOrEmpty(ext) ? ".bin" : ext));

        using var input = resolver.OpenInputStream(uri);
        if (input is null) return "";
        using var output = File.Create(target);
        input.CopyTo(output);
        return target;
    }

    private static string? QueryDisplayName(global::Android.Content.ContentResolver resolver, global::Android.Net.Uri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (index >= 0)
                    return cursor.GetString(index);
            }
        }
        catch (Exception)
        {
            // ignore
        }

        return null;
    }
}
