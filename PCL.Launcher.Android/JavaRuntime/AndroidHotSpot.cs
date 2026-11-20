using System.Runtime.InteropServices;
using System.Text;

namespace PCL.Android;

/// <summary>使用下载的 HotSpot 的 JNI 表。Android 自带的 ART JNI 不参与调用。</summary>
internal static unsafe class AndroidHotSpot
{
    private const int JniVersion16 = 0x00010006;
    private const int RtldNowGlobal = 0x102; // RTLD_NOW | RTLD_GLOBAL on 64-bit Bionic

    // JNI 规范固定的函数表下标，不能与 Java.Interop 使用的 ART JNIEnv 混用。
    private const int FindClassIndex = 6;
    private const int ExceptionDescribeIndex = 16;
    private const int DeleteLocalRefIndex = 23;
    private const int GetStaticMethodIdIndex = 113;
    private const int CallStaticVoidMethodAIndex = 143;
    private const int NewStringIndex = 163;
    private const int NewObjectArrayIndex = 172;
    private const int SetObjectArrayElementIndex = 174;
    private const int ExceptionCheckIndex = 228;
    private const int DestroyJavaVmIndex = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct JavaVmOption
    {
        public byte* OptionString;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JavaVmInitArgs
    {
        public int Version;
        public int OptionCount;
        public JavaVmOption* Options;
        public byte IgnoreUnrecognized;
    }

    [DllImport("libdl.so", EntryPoint = "dlopen")]
    private static extern nint Dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libdl.so", EntryPoint = "dlerror")]
    private static extern nint Dlerror();

    public static void Run(InstalledJavaRuntime runtime, IReadOnlyList<string> options,
        string mainClass, IReadOnlyList<string> arguments)
    {
        // Bionic 忽略发行包里的 DT_RPATH。先按依赖顺序以绝对路径载入必要库，
        // 让后续 Java 本地库可以通过 SONAME 解析。进程退出时统一释放这些映射。
        var library = runtime.LibraryDirectory;
        if (runtime.Package.Major == 8)
            Load(Path.Combine(library, "libc++_shared.so"));

        var vmLibrary = Load(runtime.JvmLibrary);
        if (runtime.Package.Major <= 11)
            Load(Path.Combine(library, "libverify.so"));
        Load(Path.Combine(library, "libjava.so"));
        if (runtime.Package.Major == 8)
            Load(Path.Combine(library, "libtinyiconv.so"));
        Load(Path.Combine(library, "libnet.so"));
        Load(Path.Combine(library, "libnio.so"));

        var create = (delegate* unmanaged<nint*, nint*, JavaVmInitArgs*, int>)
            NativeLibrary.GetExport(vmLibrary, "JNI_CreateJavaVM");
        var (vm, env) = CreateVm(create, options);
        try
        {
            CallMain(env, mainClass, arguments);
        }
        finally
        {
            var destroy = (delegate* unmanaged<nint, int>)Table(vm)[DestroyJavaVmIndex];
            // JNI 不支持在同一进程里重建 HotSpot。此服务每个进程仅启动一个 JVM。
            var result = destroy(vm);
            if (result != 0)
                Console.Error.WriteLine($"DestroyJavaVM returned {result}");
        }
    }

    private static nint Load(string path)
    {
        var handle = Dlopen(path, RtldNowGlobal);
        if (handle == 0)
            throw new DllNotFoundException($"无法加载 {Path.GetFileName(path)}：{Marshal.PtrToStringUTF8(Dlerror())}");
        return handle;
    }

    private static (nint Vm, nint Env) CreateVm(
        delegate* unmanaged<nint*, nint*, JavaVmInitArgs*, int> create, IReadOnlyList<string> options)
    {
        var byteCount = 0;
        foreach (var option in options)
        {
            if (option.Contains('\0'))
                throw new ArgumentException("JVM 选项不能包含 NUL 字符。");
            byteCount = checked(byteCount + Encoding.UTF8.GetByteCount(option) + 1);
        }

        var buffer = new byte[byteCount];
        var nativeOptions = new JavaVmOption[options.Count];
        nint vm = 0, env = 0;
        int result;
        fixed (byte* strings = buffer)
        fixed (JavaVmOption* array = nativeOptions)
        {
            var offset = 0;
            for (var index = 0; index < nativeOptions.Length; index++)
            {
                array[index].OptionString = strings + offset;
                offset += Encoding.UTF8.GetBytes(options[index], buffer.AsSpan(offset)) + 1;
            }

            var init = new JavaVmInitArgs { Version = JniVersion16, OptionCount = nativeOptions.Length, Options = array };
            result = create(&vm, &env, &init);
        }

        if (result != 0 || vm == 0 || env == 0)
            throw new InvalidOperationException($"JNI_CreateJavaVM 执行失败（{result}）。");
        return (vm, env);
    }

    private static void CallMain(nint env, string mainClass, IReadOnlyList<string> arguments)
    {
        var table = Table(env);
        var findClass = (delegate* unmanaged<nint, byte*, nint>)table[FindClassIndex];
        var getStaticMethodId = (delegate* unmanaged<nint, nint, byte*, byte*, nint>)table[GetStaticMethodIdIndex];
        var call = (delegate* unmanaged<nint, nint, nint, nint*, void>)table[CallStaticVoidMethodAIndex];
        var newString = (delegate* unmanaged<nint, char*, int, nint>)table[NewStringIndex];
        var newObjectArray = (delegate* unmanaged<nint, int, nint, nint, nint>)table[NewObjectArrayIndex];
        var setObjectArrayElement = (delegate* unmanaged<nint, nint, int, nint, void>)table[SetObjectArrayElementIndex];
        var deleteLocalRef = (delegate* unmanaged<nint, nint, void>)table[DeleteLocalRefIndex];

        nint main, stringClass, method;
        fixed (byte* name = EncodeClassName(mainClass))
            main = findClass(env, name);
        Require(env, main != 0, "查找 Java 主类");
        fixed (byte* name = "main\0"u8, signature = "([Ljava/lang/String;)V\0"u8)
            method = getStaticMethodId(env, main, name, signature);
        Require(env, method != 0, "查找 Java main(String[])");
        fixed (byte* name = "java/lang/String\0"u8)
            stringClass = findClass(env, name);
        Require(env, stringClass != 0, "查找 java.lang.String");

        var array = newObjectArray(env, arguments.Count, stringClass, 0);
        Require(env, array != 0, "创建 Java 参数数组");
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            nint value;
            fixed (char* data = argument)
                value = newString(env, data, argument.Length);
            Require(env, value != 0, "转换 Java 参数");
            setObjectArrayElement(env, array, index, value);
            deleteLocalRef(env, value);
            Require(env, true, "写入 Java 参数");
        }

        // jvalue 与指针同为 8 字节（当前仅支持 Android arm64）。
        call(env, main, method, &array);
        Require(env, true, "调用 Java main(String[])");
        deleteLocalRef(env, array);
        deleteLocalRef(env, stringClass);
        deleteLocalRef(env, main);
    }

    private static nint* Table(nint handle) => *(nint**)handle;

    private static void Require(nint env, bool success, string operation)
    {
        var table = Table(env);
        var hasException = ((delegate* unmanaged<nint, byte>)table[ExceptionCheckIndex])(env) != 0;
        if (hasException)
            ((delegate* unmanaged<nint, void>)table[ExceptionDescribeIndex])(env); // writes and clears the pending exception
        if (!success || hasException)
            throw new InvalidOperationException($"{operation}失败，请查看 JVM 日志。");
    }

    private static byte[] EncodeClassName(string name)
    {
        // FindClass 使用 modified UTF-8。补充平面字符按两个 UTF-16 代理代码单元编码。
        var output = new byte[checked(name.Length * 3 + 1)];
        var index = 0;
        foreach (var codeUnit in name)
        {
            var value = codeUnit == '.' ? '/' : codeUnit;
            if (value is > '\0' and <= '\x7f')
                output[index++] = (byte)value;
            else if (value <= '\x7ff')
            {
                output[index++] = (byte)(0xc0 | value >> 6);
                output[index++] = (byte)(0x80 | value & 0x3f);
            }
            else
            {
                output[index++] = (byte)(0xe0 | value >> 12);
                output[index++] = (byte)(0x80 | value >> 6 & 0x3f);
                output[index++] = (byte)(0x80 | value & 0x3f);
            }
        }
        return output;
    }
}
