using System;

namespace PCL;

/// <summary>
///     OAuth 完成页的内置 HTML。
///     Android 上 PCL.Launcher 的程序集资源在单视图宿主里可能取不到，这里直接内联一份；
///     图标也内联成 data URI，避免 /assets/icon 在移动端取不到资源时显示破图。
/// </summary>
internal static class OAuthCompletePage
{
    public const string Html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta content=""noindex"" name=""robots"">
    <meta content=""width=device-width, initial-scale=1.0"" name=""viewport"">
    <link id=""favicon"" rel=""icon"">
    <title>PCL 社区版提示页 / PCL Community Edition</title>
    <style>
        :root {
            --hue: 210deg;
            --sat: 85%;
            --light: 80%;
            --lighter: 90%;
            --hue1: 195deg;
            --hue2: 225deg;
            --fg: #111;
            --bg-panel: #fff8;
            @media (prefers-color-scheme: dark) {
                --light: 12%;
                --lighter: 8%;
                --fg: #eee;
                --bg-panel: #4446;
            }
        }

        body {
            --bg-root-url: none;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            color: var(--fg);
            background-image: linear-gradient(to bottom left,
            hsl(var(--hue1), var(--sat), var(--light)),
            hsl(var(--hue), var(--sat), var(--lighter)),
            hsl(var(--hue2), var(--sat), var(--light))),
            var(--bg-root-url);
            background-size: cover;
            background-position: center;
            transition: background-image 0.5s ease-in-out;
        }

        @keyframes expand {
            0% {
                transform: scaleY(0%);
            }
            30% {
                transform: scaleY(0%);
            }
            70% {
                transform: scaleY(115%)
            }
            100% {
                transform: scaleY(100%);
            }
        }

        .panel {
            max-width: 34em;
            padding: 4em;
            overflow: hidden;
            will-change: transform;
            animation: expand 0.5s forwards;
            animation-timing-function: ease-out;
            animation-play-state: paused;
            background-color: var(--bg-panel);
            border-radius: .6em;
        }

        .title {
            font-size: 2em;
            font-weight: bold;
            margin-bottom: 1em;
            display: flex;
            align-items: center;
        }

        .content {
            font-size: 1.5em;
        }

        .stacktrace {
            font-size: .5em;
            font-family: 'Cascadia Mono', 'Consolas', 'Courier New', monospace;
        }
    </style>
</head>
<body id=""root"">
<div class=""panel"" id=""panel"">
    <div class=""title"">
        <img alt=""icon"" src=""data:image/png;base64,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"" style=""margin-right:.4em"" width=""64"">
        <span id=""title"">加载中... / Loading...</span>
    </div>
    <div class=""content"" id=""content"">加载中... / Loading...</div>
    <div class=""stacktrace"" id=""stacktrace""></div>
</div>
<script>
    let root = document.getElementById(""root"");
    let panel = document.getElementById(""panel"");
    let title = document.getElementById(""title"");
    let content = document.getElementById(""content"");
    let stacktrace = document.getElementById(""stacktrace"");
    let favicon = document.getElementById(""favicon"");
    fetch(""/assets/background"").then(async response => {
        if (!response.ok) {
            console.warn(`Fetching background image returns ${response.status}`);
            return;
        }
        const blob = await response.blob();
        root.style.setProperty(""--bg-root-url"", `url(${URL.createObjectURL(blob)})`);
    });
    fetch(""/assets/icon"").then(async response => {
        if (!response.ok) {
            console.warn(`Fetching favicon image returns ${response.status}`);
            return;
        }
        const blob = await response.blob();
        favicon.href = URL.createObjectURL(blob);
    });
    fetch(""/status"").then(async response => {
        if (!response.ok) {
            console.warn(`Fetching status returns ${response.status}`);
            return;
        }
        const status = await response.json();
        if (status.success) {
            title.innerText = `欢迎回来, ${status.username}！/ Welcome back, ${status.username}!`;
            content.innerText = ""已完成登录授权，现在可以安全关闭浏览器窗口 / Authorization complete. You can now safely close this browser window."";
        } else {
            title.innerText = ""出现错误 / An error occurred"";
            title.style.color = ""red"";
            content.innerText = status.message;
            if (status.stacktrace) {
                content.style.marginBottom = ""1em"";
                stacktrace.innerText = status.stacktrace;
            }
        }
        panel.style.animationPlayState = ""running"";
    });
</script>
</body>
</html>
";
}
