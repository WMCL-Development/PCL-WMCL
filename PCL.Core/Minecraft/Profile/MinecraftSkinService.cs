using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.Core.Minecraft.Profile;

/// <summary>
///     Mojang 皮肤上传：把 PNG 作为 multipart 提交到官方接口，成功后档案会立即刷新。
/// </summary>
public static class MinecraftSkinService
{
    private const string SkinEndpoint = "https://api.minecraftservices.com/minecraft/profile/skins";

    /// <summary>上传皮肤。variant 取 classic 或 slim。</summary>
    public static async Task<bool> UploadSkinAsync(
        string accessToken,
        byte[] pngBytes,
        string variant,
        CancellationToken token = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(variant == "slim" ? "slim" : "classic"), "variant");

        var file = new ByteArrayContent(pngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "skin.png");

        try
        {
            using var response = await http.PostAsync(SkinEndpoint, content, token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                LogWrapper.Info("Profile", "皮肤上传成功");
                return true;
            }

            LogWrapper.Warn("Profile", $"皮肤上传失败：{(int)response.StatusCode} {body}");
            return false;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Profile", "皮肤上传异常：" + exception.Message);
            return false;
        }
    }

    /// <summary>读取本地 PNG 文件字节。</summary>
    public static byte[] ReadPng(string path) => File.ReadAllBytes(path);
}
