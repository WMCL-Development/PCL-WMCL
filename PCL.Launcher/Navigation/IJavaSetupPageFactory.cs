using Avalonia.Controls;

namespace PCL.Navigation;

/// <summary>允许平台提供与其 Java 运行方式匹配的设置页。</summary>
public interface IJavaSetupPageFactory
{
    Control Create();
}
