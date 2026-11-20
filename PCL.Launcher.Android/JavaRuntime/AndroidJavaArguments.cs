namespace PCL.Android;

/// <summary>将桌面 java 启动器支持的参数转换为 HotSpot Invocation API 的选项。</summary>
internal static class AndroidJavaArguments
{
    public static string[] BuildVmOptions(JavaLaunchPlan plan)
    {
        var output = new List<string>(plan.JvmOptions.Length + 2);
        var hasClassPath = false;
        var hasNatives = false;
        foreach (var option in plan.JvmOptions)
        {
            if (option.StartsWith("-Djava.library.path=", StringComparison.Ordinal))
                hasNatives = true;
        }

        for (var index = 0; index < plan.JvmOptions.Length; index++)
        {
            var option = plan.JvmOptions[index];
            switch (option)
            {
                case "-cp" or "-classpath" or "--class-path":
                    output.Add("-Djava.class.path=" + TakeValue());
                    hasClassPath = true;
                    break;
                case "-p" or "--module-path" or "--upgrade-module-path" or "--add-modules" or "--limit-modules"
                    or "--add-exports" or "--add-opens" or "--add-reads" or "--patch-module" or "--enable-native-access":
                    output.Add((option == "-p" ? "--module-path" : option) + "=" + TakeValue());
                    break;
                case "-server" or "-d64":
                    // 下载的运行时均为 64 位 HotSpot Server VM。
                    break;
                case "-jar" or "-m" or "--module" or "-client" or "-d32":
                    throw new NotSupportedException($"Android JNI 启动不支持 Java 启动器选项 {option}。");
                default:
                    if (option.StartsWith("--class-path=", StringComparison.Ordinal))
                    {
                        output.Add("-Djava.class.path=" + option["--class-path=".Length..]);
                        hasClassPath = true;
                        break;
                    }
                    if (option.StartsWith("-Djava.class.path=", StringComparison.Ordinal))
                        hasClassPath = true;
                    if (option.StartsWith('@') || option.StartsWith("--module=", StringComparison.Ordinal))
                        throw new NotSupportedException("Android JNI 启动需要显式提供主类和 JVM 选项。");
                    output.Add(option);
                    break;
            }

            string TakeValue() => ++index < plan.JvmOptions.Length
                ? plan.JvmOptions[index]
                : throw new ArgumentException($"JVM 选项 {option} 缺少值。");
        }

        if (!hasClassPath && !plan.ClassPathEntries.IsEmpty)
            output.Add("-Djava.class.path=" + string.Join(Path.PathSeparator, plan.ClassPathEntries));
        if (!hasNatives && plan.NativesDirectory is { } natives)
            output.Add("-Djava.library.path=" + natives);
        return output.ToArray();
    }
}
