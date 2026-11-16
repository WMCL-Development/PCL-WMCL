namespace PCL;

/// <summary>更新审阅窗口里的一行：资源名、版本变化说明与是否勾选。</summary>
public sealed class ResourceUpdateReviewItem(
    string name,
    string filePath,
    string description,
    bool isSelected)
{
    public string Name { get; } = name;

    public string FilePath { get; } = filePath;

    public string Description { get; } = description;

    public bool IsSelected { get; set; } = isSelected;
}
