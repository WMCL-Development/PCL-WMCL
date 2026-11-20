# Android Java 运行时

在 ARM64 设备上打开「设置 → Java」，选择 JDK 并点「运行 JVM 自检」。首次运行按需下载正式发布的 JDK，校验 SHA-256 后解压至应用私有存储；以后直接复用缓存。APK 不包含 JDK，启动器冷启动时不会下载或启动 JVM。Android x86_64 仍可使用启动器，但不提供此功能。

自检在应用专用的 `:jvm` 进程中加载 HotSpot，并调用 Java 主类，检查空参数、空格、引号、反斜杠和 Unicode 是否原样到达。成功时 Java 会打印 `WMCL_JVM_OK <version>`。HotSpot 的标准输出和错误输出写入应用私有日志，崩溃报告也保存在同一目录。

## 本地验证

正式构建使用 x86_64 Linux runner、.NET 11 SDK、Android workload、对应的 Android SDK 和构建用 JDK 21。构建 JDK 用于 Android 工具链和编译 Java 8 级别的自检类，与按需下载的游戏 JDK 无关。桌面宿主需先以同一配置构建；下方第一条下载命令只供需要 EasyTier 的包使用。

    bash scripts/fetch-easytier-android.sh arm64
    dotnet workload install android
    dotnet build PCL.Launcher/PCL.Launcher.csproj -c Release
    dotnet build PCL.Launcher.Android/PCL.Launcher.Android.csproj -c Release -r android-arm64
    dotnet run --project tests/AndroidJavaRuntime -c Release -- --skip-real

## 真机验收

1. 若设备已装正式版，使用包名为 `com.pclcommunity.pclce.jvmtest` 的调试 APK；不要用正式包名的调试签名覆盖原应用。
2. 选择 Java 21 运行自检。首次应显示下载、校验和解压，最终提示自检通过并带有 `WMCL_JVM_OK`。
3. 对同一 JDK 重跑，应直接启动 JVM，不再次下载。也可在下载或启动时取消并重试。

## 图形自检（不需要下载 JDK）

在独立调试包（包名 com.pclcommunity.pclce.gfxprobe）的「设置 → Java」点「图形自检」。这个窗口与 JVM 服务同属应用的 :jvm 进程，但不启动 HotSpot，不装载 LWJGL 或游戏。用系统 OpenGL ES 3 验证窗口、EGL 上下文和触摸：

1. 应出现蓝色画面，状态显示 WMCL_EGL_OK、分辨率和 Touch 0。
2. 点击画面：应改变底色、在触点显示黄色方块，Touch 计数增加。
3. 切到桌面再返回：应恢复画面，通常可见 EGL 上下文代数增加；触摸仍应生效。点顶部按钮应回到启动器。

如果出现黑屏或 EGL 错误，记录手机型号、Android 版本、状态整行和触发阶段。探针打印到 WMCL.Graphics 日志标签。该检查验证系统 GLES，不代表 MobileGlues、GLFW 或 Minecraft 已能运行。

首个 ARM64 图形测试包已在真机通过首帧、触摸及前后台切换测试：日志报告 OpenGL ES 3.2、WMCL_EGL_OK，EGL 上下文重建后触摸计数继续增长。首版屏幕状态栏未可靠显示，验证以画面与限定应用的日志为准。

## Java/LWJGL 窗口自检（可选调试包）

先准备固定输入，再额外传入 Android 构建参数；不设置此参数时不打包 LWJGL，并隐藏此自检入口：

    bash scripts/fetch-lwjgl-probe-android.sh --out /tmp/wmcl-lwjgl-probe
    curl -fL https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3.jar \
      -o /tmp/wmcl-lwjgl-probe/lwjgl-glfw-3.3.3.jar
    bash scripts/build-wmcl-glfw-android.sh --out /tmp/wmcl-lwjgl-probe \
      --glfw-headers /path/to/glfw-3.4/include --ndk /path/to/android-ndk-r27
    dotnet build PCL.Launcher/PCL.Launcher.csproj -c Debug
    dotnet build PCL.Launcher.Android/PCL.Launcher.Android.csproj -c Debug -r android-arm64 \
      -p:ApplicationId=com.pclcommunity.pclce.glfwprobe -p:WmclLwjglProbeAssets=/tmp/wmcl-lwjgl-probe

客户端 JDK 21 服务端窗口由宿主 EGL 提供，Java 侧调用的是官方 LWJGL GLFW 绑定；`libwmcl_glfw.so` 提供 117 个 `glfw*` 导出（官方绑定在类初始化时一次性解析全部符号，缺一即失败）。

安装独立调试包后，在「设置 → Java」点「LWJGL 3.3.3 / Java 21」。此包有独立应用数据，首次使用会按需下载 Java 21。窗口建立、`glfwSwapBuffers` 与像素校验通过时状态行显示 `WMCL_GLFW_OK · frames N`；点画面应变色、出现黄色方块，触摸计数随按下增加。退回后台会结束本次 JVM，前台 Surface 就绪后重新启动。

前一版（自有 GLES proc-table 探针）已在 ARM64 真机记录 WMCL_LWJGL_OK（OpenGL ES 3.2，3056×1044）与点按计数 2。改为官方 GLFW 绑定后的版本尚未做真机验收，当前证据只到本地 Bionic shell 的 117/117 符号与回调契约测试。调试库的哈希配对警告仍会记录。

APK 中仅打包用于诊断的 LWJGL 核心库、Java 类和许可文本；没有打包 JDK。该检查通过 LWJGL 核心 JNI 调用 Android GLES，不代表桌面 OpenGL、GLFW 全部符号、OpenAL 或 Minecraft 已可运行。Minecraft 的完整 Android 渲染、输入及游戏启动流程尚未接通。

## 原版 1.21.1 的 Android 启动素材

共享核心已实现固定 Mojang 版本清单的启动计划与下载：46 个非平台 Java 依赖，客户端 JAR 位于 classpath 最后，日志配置及资源索引独立校验。资源按 SHA-1 对象去重（当前索引 3890 个唯一对象），校验长度和摘要后才发布到应用私有缓存。Mojang 的桌面 natives 与 Linux Netty epoll 不进入 Android classpath。账号参数必须来自已认证会话，并保留为离散项；此阶段不登录或启动游戏。当前提供的五个 JDK 的 HotSpot ELF 段按 4 KiB 对齐；APK 本身通过 ZIP 16 KiB 对齐检查不代表 JDK 支持 16 KiB 页大小的设备，后者仍需独立验证。
