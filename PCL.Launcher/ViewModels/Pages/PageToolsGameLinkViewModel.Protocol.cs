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

    public void AgreeEula()
    {
        try
        {
            States.Link.LinkEula = true;
            _forceEulaPage = false;
        }
        catch (Exception ex)
        {
            _forceEulaPage = true;
            LogWrapper.Error(ex, "Link", "写入大厅协议同意状态失败");
            HintService.Show(Hints.Error(ex.Message));
        }

        RefreshLayout();
    }

    public async Task RevokeEulaAsync()
    {
        if (Interlocked.Exchange(ref _eulaBusy, 1) == 1)
            return;

        try
        {
            RefreshLayout();
            var result = await Dialog
                .ShowAsync(
                    new TextDialogRequest
                    {
                        Title = Lang.Text("Tools.GameLink.Eula.RevokeTitle"),
                        Content = Lang.Text("Tools.GameLink.Eula.RevokeConfirm"),
                        Buttons = DialogButtons.OkCancel,
                        IsWarn = true
                    })
                .ConfigureAwait(true);
            if (!result.IsConfirmed)
                return;

            _forceEulaPage = true;
            try
            {
                if (LobbyService.CurrentState == LobbyState.Connected)
                    await LobbyService.LeaveLobbyAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "Link", "撤销协议时退出大厅失败");
                HintService.Show(Hints.Error(Lang.Text("Tools.GameLink.Error.ServerExit")));
            }

            ClearNaidSession();
            try
            {
                States.Link.LinkEula = false;
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "Link", "写入大厅协议撤销状态失败");
                HintService.Show(Hints.Error(ex.Message));
            }

            HintService.Show(Hints.Info(Lang.Text("Tools.GameLink.Eula.Disabled")));
            RunOnUi(() =>
            {
                ClearFinishState();
                UpdateAccountDisplay();
                RefreshLayout();
            });
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Link", "撤销大厅协议失败");
            HintService.Show(Hints.Error(ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _eulaBusy, 0);
            RunOnUi(RefreshLayout);
        }
    }
}
