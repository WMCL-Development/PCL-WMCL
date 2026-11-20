# Android Java 运行时安装器回归测试

这是 `PCL.Launcher.Android/JavaRuntime` 的独立可移植测试工程。它直接编译真实源码
（`JavaRuntimePackage.cs`、`JavaRuntimeInstaller.cs`、`JavaRuntimeTar.cs`），
不依赖 Android workload，因此可以在普通 Linux/macOS 构建机上验证真实 JDK 归档和恶意 tar/xz。

## 运行

```bash
# 仅快速回归（自带 516 字节的最小 XZ 运行时 fixture；无需下载 JDK）
dotnet run -c Release --project tests/AndroidJavaRuntime -- --skip-real

# 完整回归：五个真实 arm64 JDK 归档 + 快速/恶意用例
dotnet run -c Release --project tests/AndroidJavaRuntime -- \
  --assets /path/to/jdk-assets

# 只跑某一个真实版本（便于性能/兼容性排查）
dotnet run -c Release --project tests/AndroidJavaRuntime -- \
  --assets /path/to/jdk-assets --real-major 17
```

`--assets` 目录需要包含：

```
jdk8-android-arm64.tar.xz
jdk11-android-arm64.tar.xz
jdk17-android-arm64.tar.xz
jdk21-android-arm64.tar.xz
jdk25-android-arm64.tar.xz
```

每个文件会先按 `JavaRuntimePackage.Catalog` 校验大小和 SHA-256，再通过真实的
`JavaRuntimeInstaller.EnsureInstalledAsync` 下载（本地 HTTP handler）、校验、解压、
发布，并检查 `FindInstalled` / 静态 `FindInstalled(root, package)`、布局、符号链接和清理结果。

## 原版 1.21.1 清单回归

加上 --vanilla-metadata /path/to/mojang-metadata 可验证原版真实清单、资源去重、参数映射与离线版本缓存，不下载客户端本体或游戏素材。目录包含官方 1.21.1.json 和 17.json；代码会核对固定的清单 SHA-256 和资源索引 SHA-1。可另加 `--vanilla-network-smoke`，从官方服务器验证下载清单、日志配置、索引及一个小资源，不会下载客户端或整套资源。

    curl -fL https://piston-meta.mojang.com/v1/packages/ff7ef54488da4a711f3c8a781a9f79105bbe801a/1.21.1.json -o /path/to/mojang-metadata/1.21.1.json
    curl -fL https://piston-meta.mojang.com/v1/packages/cad0b8152ca4f766217470e74695403d76592f92/17.json -o /path/to/mojang-metadata/17.json
    dotnet run --project tests/AndroidJavaRuntime -c Release -- --skip-real --vanilla-metadata /path/to/mojang-metadata

## 覆盖范围

- 五个真实 JDK 8/11/17/21/25 归档：JDK8 的 `jre/` 布局与 `server/libjsig.so` 符号链接；
  JDK11+ 的 `legal/` 符号链接。
- 缓存命中、marker 篡改/结构损坏后的失效与修复、无效目标目录替换。
- 同根目录并发安装只下载一次；失败/取消/部分下载/错误 SHA/长度不符/超大流后的 staging 清理。
- 原子发布：旧目标先改名为 `.retired-*`，新 payload 再改名；启动时清理遗留工作目录。
- 恶意 tar：`../`、绝对路径、反斜杠、父目录段、重复文件、符号链接逃逸/绝对路径/指向目录/悬空、
  硬链接、FIFO、条目数上限、声明大小超限、截断 XZ。
- 解压边界：条目数、单文件/总解压大小、解压后 tar 流上限、实际拷贝字节与 tar 头一致。
- Android JVM 参数桥：classpath、模块选项、natives 路径、带空格和 Unicode 的参数，以及桌面 Wrapper 不应进入 Android 的检查。

## 说明

- 测试只链接源码，不修改 `PCL.Launcher.Android.csproj`。
- 首次解包的耗时主要来自 SharpCompress 的 XZ 解码。此成本只在首次安装或缓存失效时发生；缓存命中会跳过下载和解压。
- Android 下载由安装器统一控制 15 分钟超时；会话不会另外启用 `HttpClient` 默认的 100 秒超时。
- 测试里的 `Fixtures/minimal-runtime.tar.xz` 是故意构造的最小合法运行时，仅用于快速回归，
  不是可启动的 JDK。
