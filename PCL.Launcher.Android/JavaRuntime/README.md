# Android Java 运行时

Android 上没有系统 Java，运行时由启动器自己下载到应用目录。APK 不含 JDK，
冷启动也不会下载或启动 JVM，只有真正需要时才安装。

## 使用方式

「设置 → Java → 添加」提供两项：

- **联网下载 JDK**：列出目录中的版本（Java 8 / 11 / 17 / 21 / 25），下载对应
  `jdk<N>-android-arm64.tar.xz`，按固定 SHA-256 校验后解压到
  `<NoBackupFilesDir>/pcl/java`，再把结果注册到 Java 列表。
- **导入 JDK**：选择已有的运行时压缩包，解压到 `<NoBackupFilesDir>/pcl/java/imported`。

两项都由共享页面 `PageSetupJava` 发起，Android 通过 `IJavaRuntimeProvider` 实现具体下载，
页面本身没有 Android 分支。桌面端不注册该接口，回退到启动器原有的下载逻辑。

## 安装细节

`JavaRuntimeInstaller` 负责下载与发布：

- 缓存命中先于安装锁，每个根目录一把 `SemaphoreSlim`；
- marker 精确校验 SHA-256，并确认 `release` 的版本与架构、`bin/java`、`modules`、
  `libjvm.so` 均为非空文件，结构损坏会使缓存失效并重装；
- 下载边写边算哈希，校验 Content-Length 与总大小，15 分钟超时；
- 原子发布：旧目标先改名 `.retired-*`，完整 payload 再改名到目标，
  残留的 `.install-*` / `.retired-*` 在线程池清理。

`JavaRuntimeTar` 负责解包，带硬上限并拒绝越界条目：

| 限制 | 值 |
| --- | --- |
| 单文件解压 | 512 MiB |
| 总解压 | 1 GiB |
| 条目数 | 4096 |
| 路径长度 | 4096 |

拒绝 `../`、绝对路径、反斜杠、盘符、重复项、硬链接、FIFO 与设备文件；
符号链接必须落在安装目录内且指向包内普通文件。

## 本地验证

    dotnet run --project tests/AndroidJavaRuntime -c Release -- --skip-real

36 项测试覆盖 JDK 目录、安装与解包边界、Android 库过滤等。加 `--real-major 21`
会实际下载对应版本并校验；该开关需要网络。

## 历史

早期版本在「设置 → Java」里带过两个诊断入口（JVM 自检、图形自检），用于在真机上分别验证
HotSpot 装载与系统 GLES 窗口。Java 页统一到桌面版式后，两者都不再有入口，因此代码也已移除：

- JVM 自检的 Java 主类：`git show 261a48f:PCL.Launcher.Android/JavaRuntime/Probe/WmclJvmProbe.java`
- 图形自检 Activity：`git show 5efb95e:PCL.Launcher.Android/Graphics/AndroidGraphicsProbeActivity.cs`

需要复现时按上面的命令取回即可，不需要常驻在主干。
