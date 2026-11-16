using System;
using System.Threading;
using System.Threading.Tasks;
using PCL;
using PCL.Core.Minecraft.IdentityModel;
using PCL.Core.Minecraft.IdentityModel.OAuth;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Authentication;

namespace PCL.Cli;

/// <summary>
///     终端版账号交互：设备码登录直接打印到控制台并轮询，其余错误处理退化为「不处理」。
///     与界面层的 IProfileUi 实现职责相同。
/// </summary>
internal sealed class CliProfileUi : IProfileUi
{
    public GameLaunch.McLoginData? GetLoginData()
    {
        var profile = ProfileService.Current;
        return profile is null ? new GameLaunch.McLoginLegacy() : new GameLaunch.McLoginMs();
    }

    public string IsProfileValid() => string.Empty;

    public async Task<AuthorizeResult?> ShowDeviceCodeLoginAsync(
        DeviceCodeAuthenticationContext context,
        CancellationToken token)
    {
        CliConsole.Line("请在浏览器中打开：" + context.Data.VerificationUri);
        CliConsole.Line("输入代码：" + context.Data.UserCode);
        CliConsole.Line("等待你在浏览器里确认…");
        var result = await context.PollAsync(token).ConfigureAwait(false);
        if (result is null) CliConsole.Error("登录未完成或已取消");
        return result;
    }

    public bool HandleMicrosoftXstsError(IdentityModelAuthenticationException exception) => false;

    public bool HandleMicrosoftNotOwnedError(IdentityModelAuthenticationException exception) => false;

    public bool HandleMicrosoftCreateProfileError(IdentityModelAuthenticationException exception) => false;
}
