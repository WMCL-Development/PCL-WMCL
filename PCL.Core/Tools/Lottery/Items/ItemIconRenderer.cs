using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using PCL.Core.App;
using SkiaSharp;

namespace PCL.Core.Tools.Lottery.Items;

/// <summary>
/// 把物品模型渲染成接近物品栏的图标：
/// 方块按模型元素做等距投影并贴图，普通物品仍使用平面纹理；
/// 无法从模型还原的物品（如 builtin/entity）由调用方回退到平面纹理。
/// </summary>
/// <summary>一个带贴图的面：目标四点（画布像素坐标）+ 贴图区域 + 着色。</summary>
public sealed record ItemQuad(SKBitmap Texture, SKRect Source, SKPoint[] Points, SKColor Color);

internal sealed class ItemIconRenderer : IDisposable
{
    /// <summary>渲染画布尺寸，由调用方按物品栏缩放决定（16 / 32 / 48）。</summary>
    private readonly int _iconSize;
    private List<ItemQuad>? _collector;
    private const float ModelSize = 16f;

    private sealed record Face(string TextureReference, int? TintIndex);

    private sealed record Element(
        float X0, float Y0, float Z0, float X1, float Y1, float Z1,
        IReadOnlyDictionary<string, Face> Faces);

    private sealed class Model
    {
        public Dictionary<string, string> Textures { get; } = new(StringComparer.Ordinal);
        public List<Element> Elements { get; } = [];
    }

    private readonly IReadOnlyDictionary<string, ZipArchiveEntry> _index;
    private readonly Dictionary<string, SKBitmap?> _bitmapCache = new(StringComparer.Ordinal);

    public ItemIconRenderer(IReadOnlyDictionary<string, ZipArchiveEntry> index, int iconSize)
    {
        _index = index;
        _iconSize = iconSize;
    }

    public void Dispose()
    {
        foreach (var bitmap in _bitmapCache.Values) bitmap?.Dispose();
        _bitmapCache.Clear();
    }

    /// <summary>渲染模型图标并写入缓存目录；模型不可渲染时返回 null。</summary>
    public string? TryRender(ZipArchiveEntry modelEntry, string ns, string id)
    {
        try
        {
            var model = LoadModel(modelEntry, ns);
            if (model is null || model.Elements.Count == 0)
                return null;

            return RenderAndSave(ns, id, canvas => DrawModel(canvas, model, ns, id));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>用单张纹理合成等距方块图标，用于 builtin/entity 物品的近似显示。</summary>
    public string? TryRenderCube(ZipArchiveEntry textureEntry, string ns, string id)
    {
        try
        {
            var bitmap = Decode(textureEntry);
            if (bitmap is null || !IsMostlyOpaque(bitmap)) return null;
            return RenderAndSave(ns, id, canvas => DrawCube(canvas, bitmap, id));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>头颅类物品：从皮肤/实体图集裁出前、右、上三个面拼成等距方块。</summary>
    public string? TryRenderHead(ZipArchiveEntry textureEntry, string ns, string id)
    {
        try
        {
            var bitmap = Decode(textureEntry);
            if (bitmap is null || bitmap.Width < 32 || bitmap.Height < 16) return null;
            return RenderAndSave(ns, id, canvas =>
            {
                var face = new Face("", null);
                var front = new SKRect(8, 8, 16, 16);
                var right = new SKRect(0, 8, 8, 16);
                var top = new SKRect(8, 0, 16, 8);
                var drew = DrawTexturedFace(canvas, bitmap, face, id, 0.8f, right,
                    (16, 16, 0), (16, 16, 16), (16, 0, 16), (16, 0, 0));
                drew |= DrawTexturedFace(canvas, bitmap, face, id, 0.6f, front,
                    (0, 16, 16), (16, 16, 16), (16, 0, 16), (0, 0, 16));
                drew |= DrawTexturedFace(canvas, bitmap, face, id, 1f, top,
                    (0, 16, 0), (16, 16, 0), (16, 16, 16), (0, 16, 16));
                return drew;
            });
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record HeadCube(
        float X0, float Y0, float Z0, float X1, float Y1, float Z1,
        int U, int V, int Width, int Height, int Depth);

    /// <summary>
    /// 龙首：按末影龙头部几何（主头 + 吻部 + 四只角）和各自的 UV，
    /// 从实体图集做等距渲染，避免只画一个没有鼻吻和角的方块。
    /// </summary>
    public string? TryRenderDragonHead(ZipArchiveEntry textureEntry, string ns, string id)
    {
        try
        {
            var bitmap = Decode(textureEntry);
            if (bitmap is null) return null;

            // 坐标沿用 Java 版头部几何（整体上移 24 避免负坐标），z 轴取反让吻部朝向观察者。
            HeadCube[] cubes =
            [
                new(-8, 16, -6, 8, 32, 10, 112, 30, 16, 16, 16),   // 主头
                new(8, 24, -8, 18, 29, 8, 176, 44, 12, 5, 16),     // 上颚/吻部（朝右）
                new(8, 20, -8, 18, 24, 8, 176, 65, 12, 4, 16),     // 下颚
                new(-5, 32, -2, -3, 36, 4, 0, 0, 2, 4, 6),         // 左角
                new(3, 32, -2, 5, 36, 4, 0, 0, 2, 4, 6)            // 右角
            ];

            return RenderAndSave(ns, id, canvas =>
            {
                var drew = DrawHeadCubes(canvas, bitmap, id, cubes);
                return drew;
            });
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool DrawHeadCubes(
        SKCanvas canvas, SKBitmap bitmap, string id, IReadOnlyList<HeadCube> cubes)
    {
        static SKPoint Raw(float x, float y, float z) =>
            new((x - z) * 0.8660254f, (x + z) * 0.5f - y);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        var corners = new List<SKPoint>(cubes.Count * 8);
        foreach (var cube in cubes)
        {
            foreach (var (x, y, z) in Corners(cube))
            {
                var point = Raw(x, y, z);
                corners.Add(point);
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }
        }
        if (corners.Count == 0) return false;

        var width = Math.Max(1f, maxX - minX);
        var height = Math.Max(1f, maxY - minY);
        var scale = (_iconSize - 4f) / Math.Max(width, height);
        var offsetX = (_iconSize - width * scale) / 2f - minX * scale;
        var offsetY = (_iconSize - height * scale) / 2f - minY * scale;
        SKPoint Project(float x, float y, float z)
        {
            var point = Raw(x, y, z);
            return new SKPoint(point.X * scale + offsetX, point.Y * scale + offsetY);
        }

        var face = new Face("", null);
        var drew = false;
        // 按细节先后绘制，避免吻部/龙角被主头盖住。
        foreach (var cube in cubes)
        {
            // +z 面在翻转后对应原始 north 面（正面/眼睛）。
            var east = new SKRect(cube.U, cube.V + cube.Depth,
                cube.U + cube.Depth, cube.V + cube.Depth + cube.Height);
            var south = new SKRect(cube.U + cube.Depth, cube.V + cube.Depth,
                cube.U + cube.Depth + cube.Width, cube.V + cube.Depth + cube.Height);
            var up = new SKRect(cube.U + cube.Depth, cube.V,
                cube.U + cube.Depth + cube.Width, cube.V + cube.Depth);

            drew |= DrawHeadFace(canvas, bitmap, face, id, Project,
                cube.Width <= 2 ? 0.25f : 0.8f, east,
                (cube.X1, cube.Y1, cube.Z0), (cube.X1, cube.Y1, cube.Z1),
                (cube.X1, cube.Y0, cube.Z1), (cube.X1, cube.Y0, cube.Z0));
            if (cube.Width == 16 && cube.Height == 16 && cube.Depth == 16)
            {
                // 主头正面用干净的深色面 + 两只紫色眼睛，避免把图集里的嘴部一起放大成色块。
                DrawDragonFace(canvas, Project, cube);
                drew = true;
            }
            else
            {
                drew |= DrawHeadFace(canvas, bitmap, face, id, Project,
                    cube.Width <= 2 ? 0.25f : 0.65f, south,
                    (cube.X0, cube.Y1, cube.Z1), (cube.X1, cube.Y1, cube.Z1),
                    (cube.X1, cube.Y0, cube.Z1), (cube.X0, cube.Y0, cube.Z1));
            }
            // 下颚的顶面是口腔纹理，等距视角下不应露出来。
            if (!(cube.Width == 12 && cube.Height == 4))
                drew |= DrawHeadFace(canvas, bitmap, face, id, Project,
                    cube.Width <= 2 ? 0.3f : 1f, up,
                    (cube.X0, cube.Y1, cube.Z0), (cube.X1, cube.Y1, cube.Z0),
                    (cube.X1, cube.Y1, cube.Z1), (cube.X0, cube.Y1, cube.Z1));
        }
        return drew;
    }

    private static IEnumerable<(float X, float Y, float Z)> Corners(HeadCube cube)
    {
        yield return (cube.X0, cube.Y0, cube.Z0);
        yield return (cube.X1, cube.Y0, cube.Z0);
        yield return (cube.X0, cube.Y1, cube.Z0);
        yield return (cube.X1, cube.Y1, cube.Z0);
        yield return (cube.X0, cube.Y0, cube.Z1);
        yield return (cube.X1, cube.Y0, cube.Z1);
        yield return (cube.X0, cube.Y1, cube.Z1);
        yield return (cube.X1, cube.Y1, cube.Z1);
    }

    private bool DrawHeadFace(
        SKCanvas canvas, SKBitmap bitmap, Face face, string id,
        Func<float, float, float, SKPoint> project, float shade, SKRect source,
        params (float X, float Y, float Z)[] points)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var shader = SKShader.CreateImage(image, SKShaderTileMode.Clamp,
            SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            SKMatrix.Identity);
        using var paint = new SKPaint { IsAntialias = false, Shader = shader };
        var color = Multiply(SKColors.White, shade);
        var positions = points.Select(point => project(point.X, point.Y, point.Z)).ToArray();
        var texturePoints = new[]
        {
            new SKPoint(source.Left, source.Top),
            new SKPoint(source.Right, source.Top),
            new SKPoint(source.Right, source.Bottom),
            new SKPoint(source.Left, source.Bottom)
        };
        var vertexPoints = new[]
        {
            positions[0], positions[1], positions[2],
            positions[0], positions[2], positions[3]
        };
        var textureIndices = new[]
        {
            texturePoints[0], texturePoints[1], texturePoints[2],
            texturePoints[0], texturePoints[2], texturePoints[3]
        };
        if (_collector is not null)
        {
            // 复制一份贴图：渲染器释放时会 Dispose 缓存位图，四边形要自己持有。
            _collector.Add(new ItemQuad(bitmap.Copy(), source, positions, color));
            return true;
        }

        var colors = new[] { color, color, color, color, color, color };
        using var vertices = SKVertices.CreateCopy(
            SKVertexMode.Triangles, vertexPoints, textureIndices, colors);
        canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        return true;
    }

    private static void DrawDragonFace(
        SKCanvas canvas, Func<float, float, float, SKPoint> project, HeadCube cube)
    {
        var topLeft = project(cube.X0, cube.Y1, cube.Z1);
        var topRight = project(cube.X1, cube.Y1, cube.Z1);
        var bottomRight = project(cube.X1, cube.Y0, cube.Z1);
        var bottomLeft = project(cube.X0, cube.Y0, cube.Z1);

        using var facePaint = new SKPaint
        {
            Color = new SKColor(0x20, 0x20, 0x20),
            IsAntialias = false
        };
        using var path = new SKPath();
        path.AddPoly([topLeft, topRight, bottomRight, bottomLeft], true);
        canvas.DrawPath(path, facePaint);

        SKPoint Point(float u, float v) => new(
            topLeft.X + (topRight.X - topLeft.X) * u + (bottomLeft.X - topLeft.X) * v,
            topLeft.Y + (topRight.Y - topLeft.Y) * u + (bottomLeft.Y - topLeft.Y) * v);

        using var eyePaint = new SKPaint
        {
            Color = new SKColor(0xC5, 0x6C, 0xFF),
            IsAntialias = false
        };
        foreach (var (u0, u1) in new[] { (0.15f, 0.32f), (0.65f, 0.82f) })
        {
            using var eye = new SKPath();
            eye.AddPoly([Point(u0, 0.30f), Point(u1, 0.30f),
                Point(u1, 0.42f), Point(u0, 0.42f)], true);
            canvas.DrawPath(eye, eyePaint);
        }
    }

    /// <summary>刷怪蛋：灰度底图 + 斑点层分别按主/次色着色，还原物品栏颜色。</summary>
    public string? TryRenderSpawnEgg(
        ZipArchiveEntry baseEntry, ZipArchiveEntry overlayEntry,
        uint primary, uint secondary, string ns, string id)
    {
        try
        {
            var baseBitmap = Decode(baseEntry);
            var overlayBitmap = Decode(overlayEntry);
            if (baseBitmap is null || overlayBitmap is null) return null;
            return RenderAndSave(ns, id, canvas =>
            {
                DrawFlat(canvas, baseBitmap, FromRgb(primary));
                DrawFlat(canvas, overlayBitmap, FromRgb(secondary));
                return true;
            });
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void DrawFlat(SKCanvas canvas, SKBitmap bitmap, SKColor color) =>
        DrawFlatRegion(canvas, bitmap,
            new SKRect(0, 0, bitmap.Width, bitmap.Height), color);

    private void DrawFlatRegion(
        SKCanvas canvas, SKBitmap bitmap, SKRect source, SKColor color)
    {
        if (_collector is not null)
        {
            _collector.Add(new ItemQuad(bitmap.Copy(), source,
            [
                new SKPoint(0, 0), new SKPoint(_iconSize, 0),
                new SKPoint(_iconSize, _iconSize), new SKPoint(0, _iconSize)
            ], color));
            return;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { IsAntialias = false };
        if (color != SKColors.White)
            paint.ColorFilter = SKColorFilter.CreateBlendMode(color, SKBlendMode.Modulate);
        canvas.DrawImage(
            image, source, new SKRect(0, 0, _iconSize, _iconSize),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
    }

    private static SKColor FromRgb(uint value) =>
        new((byte)(value >> 16), (byte)(value >> 8), (byte)value);

    /// <summary>模型链是否以 builtin/entity 结尾（这类物品由游戏代码渲染，jar 里没有静态图标）。</summary>
    public bool IsBuiltinEntity(ZipArchiveEntry entry, string defaultNs)
    {
        ZipArchiveEntry? current = entry;
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            using var document = TryReadJson(current);
            if (document is null) return false;
            if (!document.RootElement.TryGetProperty("parent", out var parent) ||
                parent.ValueKind != JsonValueKind.String ||
                parent.GetString() is not { Length: > 0 } parentPath)
                return false;
            if (parentPath is "builtin/entity" or "minecraft:builtin/entity") return true;
            current = FindModelEntry(parentPath, defaultNs);
        }
        return false;
    }

    /// <summary>把模型渲染成四边形列表，供界面按任意尺寸实时光栅化。</summary>
    public IReadOnlyList<ItemQuad>? CollectQuads(ZipArchiveEntry modelEntry, string ns, string id)
    {
        try
        {
            var model = LoadModel(modelEntry, ns);
            if (model is null || model.Elements.Count == 0) return null;
            return Collect(canvas => DrawModel(canvas, model, ns, id));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>单张纹理的等距方块四边形。</summary>
    public IReadOnlyList<ItemQuad>? CollectCubeQuads(ZipArchiveEntry textureEntry, string ns, string id)
    {
        try
        {
            var bitmap = Decode(textureEntry);
            if (bitmap is null || !IsMostlyOpaque(bitmap)) return null;
            return Collect(canvas => DrawCube(canvas, bitmap, id));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private List<ItemQuad>? Collect(Func<SKCanvas, bool> draw)
    {
        _collector = [];
        try
        {
            using var surface = SKSurface.Create(
                new SKImageInfo(_iconSize, _iconSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null) return null;
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            if (!draw(canvas)) return null;
            return _collector.Count > 0 ? _collector : null;
        }
        finally
        {
            _collector = null;
        }
    }

    private string? RenderAndSave(string ns, string id, Func<SKCanvas, bool> draw)
    {
        using var surface = SKSurface.Create(
            new SKImageInfo(_iconSize, _iconSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (!draw(canvas)) return null;

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) return null;

        var directory = Path.Combine(Paths.SharedLocalData, "lottery", "item-icons");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, SafeName(ns) + "_" + SafeName(id) + ".png");
        using (var output = File.Create(target))
            data.SaveTo(output);
        return target;
    }

    private Model? LoadModel(ZipArchiveEntry entry, string defaultNs)
    {
        var model = new Model();
        ZipArchiveEntry? current = entry;
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            using var document = TryReadJson(current);
            if (document is null) break;
            var root = document.RootElement;

            if (root.TryGetProperty("textures", out var textures) &&
                textures.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in textures.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String) continue;
                    if (property.Value.GetString() is not { Length: > 0 } value) continue;
                    model.Textures.TryAdd(property.Name, value);
                }
            }

            if (model.Elements.Count == 0 &&
                root.TryGetProperty("elements", out var elements) &&
                elements.ValueKind == JsonValueKind.Array)
                ParseElements(elements, model);

            current = root.TryGetProperty("parent", out var parent) &&
                      parent.ValueKind == JsonValueKind.String &&
                      parent.GetString() is { Length: > 0 } parentPath
                ? FindModelEntry(parentPath, defaultNs)
                : null;
        }
        return model.Textures.Count == 0 && model.Elements.Count == 0 ? null : model;
    }

    private static void ParseElements(JsonElement elements, Model model)
    {
        foreach (var element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            // 只跳过真正旋转的元素（花、火把等）；angle=0 的旋转在新版模型里很常见，按轴对齐处理。
            if (element.TryGetProperty("rotation", out var rotation) &&
                rotation.ValueKind == JsonValueKind.Object &&
                rotation.TryGetProperty("angle", out var angleElement) &&
                angleElement.TryGetDouble(out var angle) &&
                Math.Abs(angle) > 0.01)
                continue;
            if (!TryVector(element, "from", out var from) || !TryVector(element, "to", out var to))
                continue;
            if (!element.TryGetProperty("faces", out var faces) ||
                faces.ValueKind != JsonValueKind.Object)
                continue;

            var parsedFaces = new Dictionary<string, Face>(StringComparer.Ordinal);
            foreach (var face in faces.EnumerateObject())
            {
                if (face.Value.ValueKind != JsonValueKind.Object) continue;
                if (!face.Value.TryGetProperty("texture", out var texture) ||
                    texture.ValueKind != JsonValueKind.String ||
                    texture.GetString() is not { Length: > 0 } reference)
                    continue;
                int? tint = face.Value.TryGetProperty("tintindex", out var tintElement) &&
                            tintElement.TryGetInt32(out var tintValue)
                    ? tintValue
                    : null;
                parsedFaces[face.Name] = new Face(reference, tint);
            }
            if (parsedFaces.Count == 0) continue;

            model.Elements.Add(new Element(
                Math.Min(from[0], to[0]), Math.Min(from[1], to[1]), Math.Min(from[2], to[2]),
                Math.Max(from[0], to[0]), Math.Max(from[1], to[1]), Math.Max(from[2], to[2]),
                parsedFaces));
        }
    }

    private static bool TryVector(JsonElement element, string name, out float[] values)
    {
        values = [];
        if (!element.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 3)
            return false;
        var result = new float[3];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetSingle(out result[index]))
                return false;
            index++;
        }
        values = result;
        return true;
    }

    /// <summary>
    ///     原版物品图标的 2:1 等轴测投影：顶面菱形 16 宽 8 高、两侧面各 8 宽。
    ///     模型坐标 0..16，输出画布按 _iconSize/16 整数倍放大，采样为最近邻，保持像素对齐。
    /// </summary>
    private SKPoint Project(float x, float y, float z)
    {
        // 与原版 GUI 图标一致的比例：立方体轮廓略高于宽（约 17.9 × 20），
        // 顶面菱形约占 40% 高度，两个侧面各占约 60%。
        const float half = 8f;
        const float axisX = 0.56f;
        const float axisZ = 0.25f;
        const float axisY = 0.75f;
        const float width = 32f * axisX;
        const float height = 32f * axisZ + 16f * axisY;

        var cx = x - half;
        var cy = y - half;
        var cz = z - half;

        var sx = (cx - cz) * axisX;
        var sy = (cx + cz) * axisZ - cy * axisY;

        var scale = (_iconSize - 2f) / Math.Max(width, height);
        var center = _iconSize / 2f;
        return new SKPoint(center + sx * scale, center + sy * scale);
    }

    /// <summary>按远到近绘制元素，只画等距视角可见的 up/east/south 三个面。</summary>
    private bool DrawModel(SKCanvas canvas, Model model, string ns, string id)
    {
        var drew = false;
        foreach (var element in model.Elements
                     .OrderBy(item => item.X1 + item.Y1 + item.Z1))
        {
            drew |= DrawFace(canvas, model, ns, id, element,
                "east", 0.8f,
                (element.X1, element.Y1, element.Z0),
                (element.X1, element.Y1, element.Z1),
                (element.X1, element.Y0, element.Z1),
                (element.X1, element.Y0, element.Z0));
            drew |= DrawFace(canvas, model, ns, id, element,
                "south", 0.6f,
                (element.X0, element.Y1, element.Z1),
                (element.X1, element.Y1, element.Z1),
                (element.X1, element.Y0, element.Z1),
                (element.X0, element.Y0, element.Z1));
            drew |= DrawFace(canvas, model, ns, id, element,
                "up", 1f,
                (element.X0, element.Y1, element.Z0),
                (element.X1, element.Y1, element.Z0),
                (element.X1, element.Y1, element.Z1),
                (element.X0, element.Y1, element.Z1));
        }
        return drew;
    }

    /// <summary>单纹理整方块，给 builtin/entity 物品做近似图标。</summary>
    private bool DrawCube(SKCanvas canvas, SKBitmap bitmap, string id)
    {
        var face = new Face("", null);
        var drew = false;
        drew |= DrawTexturedFace(canvas, bitmap, face, id, 0.8f,
            (16, 16, 0), (16, 16, 16), (16, 0, 16), (16, 0, 0));
        drew |= DrawTexturedFace(canvas, bitmap, face, id, 0.6f,
            (0, 16, 16), (16, 16, 16), (16, 0, 16), (0, 0, 16));
        drew |= DrawTexturedFace(canvas, bitmap, face, id, 1f,
            (0, 16, 0), (16, 16, 0), (16, 16, 16), (0, 16, 16));
        return drew;
    }

    private bool DrawFace(
        SKCanvas canvas, Model model, string ns, string id, Element element,
        string faceName, float shade,
        params (float X, float Y, float Z)[] corners)
    {
        if (!element.Faces.TryGetValue(faceName, out var face)) return false;
        if (face.TextureReference.Contains("overlay", StringComparison.OrdinalIgnoreCase))
            return false;
        var reference = ResolveTextureReference(model.Textures, face.TextureReference);
        if (reference is null) return false;
        var bitmap = LoadTexture(reference, ns);
        if (bitmap is null) return false;
        return DrawTexturedFace(canvas, bitmap, face, id, shade, corners);
    }

    private bool DrawTexturedFace(
        SKCanvas canvas, SKBitmap bitmap, Face face, string id, float shade,
        params (float X, float Y, float Z)[] corners) =>
        DrawTexturedFace(canvas, bitmap, face, id, shade, null, corners);

    private bool DrawTexturedFace(
        SKCanvas canvas, SKBitmap bitmap, Face face, string id, float shade,
        SKRect? sourceRect, params (float X, float Y, float Z)[] corners)
    {
        var source = sourceRect ?? new SKRect(0, 0, bitmap.Width, bitmap.Height);
        using var image = SKImage.FromBitmap(bitmap);
        using var shader = SKShader.CreateImage(image, SKShaderTileMode.Clamp,
            SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            SKMatrix.Identity);
        using var paint = new SKPaint { IsAntialias = false, Shader = shader };

        var tint = face.TintIndex is >= 0 ? TintFor(id) : null;
        var color = Multiply(tint ?? SKColors.White, shade);
        var positions = new[]
        {
            Project(corners[0].X, corners[0].Y, corners[0].Z),
            Project(corners[1].X, corners[1].Y, corners[1].Z),
            Project(corners[2].X, corners[2].Y, corners[2].Z),
            Project(corners[3].X, corners[3].Y, corners[3].Z)
        };
        var texturePoints = new[]
        {
            new SKPoint(source.Left, source.Top),
            new SKPoint(source.Right, source.Top),
            new SKPoint(source.Right, source.Bottom),
            new SKPoint(source.Left, source.Bottom)
        };
        var vertexPoints = new[]
        {
            positions[0], positions[1], positions[2],
            positions[0], positions[2], positions[3]
        };
        var textureIndices = new[]
        {
            texturePoints[0], texturePoints[1], texturePoints[2],
            texturePoints[0], texturePoints[2], texturePoints[3]
        };
        if (_collector is not null)
        {
            // 复制一份贴图：渲染器释放时会 Dispose 缓存位图，四边形要自己持有。
            _collector.Add(new ItemQuad(bitmap.Copy(), source, positions, color));
            return true;
        }

        var colors = new[] { color, color, color, color, color, color };
        using var vertices = SKVertices.CreateCopy(
            SKVertexMode.Triangles, vertexPoints, textureIndices, colors);
        canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        return true;
    }

    private static string? ResolveTextureReference(
        IReadOnlyDictionary<string, string> textures, string reference)
    {
        var current = reference;
        for (var depth = 0; depth < 6 && current.StartsWith('#'); depth++)
        {
            if (!textures.TryGetValue(current[1..], out var next)) return null;
            current = next;
        }
        return current.StartsWith('#') ? null : current;
    }

    private SKBitmap? LoadTexture(string reference, string defaultNs)
    {
        var separator = reference.IndexOf(':');
        var ns = separator < 0 ? defaultNs : reference[..separator];
        var path = separator < 0 ? reference : reference[(separator + 1)..];
        var key = "assets/" + ns + "/textures/" + path + ".png";
        if (_bitmapCache.TryGetValue(key, out var cached)) return cached;

        var bitmap = _index.TryGetValue(key, out var entry) ? Decode(entry) : null;
        _bitmapCache[key] = bitmap;
        return bitmap;
    }

    private static SKBitmap? Decode(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0 || entry.Length > 4 * 1024 * 1024) return null;
        try
        {
            using var stream = entry.Open();
            return SKBitmap.Decode(stream);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>稀疏纹理（如潮涌核心的粒子图）拼成立方体会出现碎片，只对不透明纹理做立方体近似。</summary>
    private static bool IsMostlyOpaque(SKBitmap bitmap)
    {
        if (bitmap.Width == 0 || bitmap.Height == 0) return false;
        var opaque = 0;
        var total = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                total++;
                if (bitmap.GetPixel(x, y).Alpha > 128) opaque++;
            }
        }
        return total > 0 && opaque * 100 >= total * 60;
    }

    private ZipArchiveEntry? FindModelEntry(string reference, string defaultNs)
    {
        var separator = reference.IndexOf(':');
        var ns = separator < 0 ? defaultNs : reference[..separator];
        var path = separator < 0 ? reference : reference[(separator + 1)..];
        return _index.GetValueOrDefault("assets/" + ns + "/models/" + path + ".json");
    }

    /// <summary>物品栏里的默认群系着色；只对带 tintindex 的面生效。</summary>
    private static SKColor? TintFor(string id)
    {
        var name = id.ToLowerInvariant();
        if (name.Contains("grass_block")) return new SKColor(0x79, 0xC0, 0x5A);
        if (name.Contains("spruce_leaves")) return new SKColor(0x61, 0x99, 0x61);
        if (name.Contains("birch_leaves")) return new SKColor(0x80, 0xA7, 0x55);
        if (name.Contains("jungle_leaves")) return new SKColor(0x59, 0xC9, 0x3C);
        if (name.Contains("acacia_leaves")) return new SKColor(0x67, 0x75, 0x34);
        if (name.Contains("dark_oak_leaves")) return new SKColor(0x4E, 0x7F, 0x2E);
        if (name.Contains("mangrove_leaves")) return new SKColor(0x6A, 0x70, 0x39);
        if (name.Contains("cherry_leaves")) return new SKColor(0xF5, 0xA0, 0xC0);
        if (name.Contains("azalea_leaves")) return new SKColor(0x6A, 0x9C, 0x4A);
        if (name.Contains("leaves")) return new SKColor(0x48, 0xB5, 0x18);
        if (name.Contains("vine")) return new SKColor(0x48, 0xB5, 0x18);
        if (name.Contains("water")) return new SKColor(0x3F, 0x76, 0xE4);
        if (name.Contains("lily_pad")) return new SKColor(0x20, 0x80, 0x30);
        if (name.Contains("sugar_cane")) return new SKColor(0x8C, 0xBB, 0x5A);
        return null;
    }

    private static SKColor Multiply(SKColor color, float factor) => new(
        (byte)Math.Clamp(color.Red * factor, 0, 255),
        (byte)Math.Clamp(color.Green * factor, 0, 255),
        (byte)Math.Clamp(color.Blue * factor, 0, 255),
        color.Alpha);

    private static JsonDocument? TryReadJson(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0 || entry.Length > 1024 * 1024) return null;
        try
        {
            using var stream = entry.Open();
            return JsonDocument.Parse(stream);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static string SafeName(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
}
