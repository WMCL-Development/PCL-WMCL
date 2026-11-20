using System.Text;

namespace PCL.Android.Tests;

/// <summary>最小 ustar 写入器，用来构造真实 tar/xz 解压器无法直接生成的恶意归档。</summary>
internal sealed class TarFixture
{
    private const int HeaderSize = 512;
    private readonly MemoryStream _output = new();

    public TarFixture AddFile(string name, string content,
        UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite) =>
        AddBytes(name, Encoding.UTF8.GetBytes(content), mode);

    public TarFixture AddBytes(string name, byte[] data, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite) =>
        AddEntry((byte)'0', name, string.Empty, data.LongLength, data, mode);

    public TarFixture AddDirectory(string name, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute) =>
        AddEntry((byte)'5', name.EndsWith('/') ? name : name + "/", string.Empty, 0, null, mode);

    public TarFixture AddSymlink(string name, string target) =>
        AddEntry((byte)'2', name, target, 0, null, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    public TarFixture AddHardLink(string name, string target) =>
        AddEntry((byte)'1', name, target, 0, null, UnixFileMode.UserRead | UnixFileMode.UserWrite);

    public TarFixture AddFifo(string name) =>
        AddEntry((byte)'6', name, string.Empty, 0, null, UnixFileMode.UserRead | UnixFileMode.UserWrite);

    public TarFixture AddRawEntry(char type, string name, string linkName = "", long size = 0, byte[]? data = null,
        UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite) =>
        AddEntry((byte)type, name, linkName, data is not null && size == 0 ? data.LongLength : size, data, mode);

    public MemoryStream Build(bool appendEndBlocks = true)
    {
        if (appendEndBlocks)
            _output.Write(new byte[HeaderSize * 2]);
        return new MemoryStream(_output.ToArray());
    }

    private TarFixture AddEntry(byte type, string name, string linkName, long size, byte[]? data, UnixFileMode mode)
    {
        var header = new byte[HeaderSize];
        WriteString(header, 0, 100, name);
        WriteOctal(header, 100, 8, (long)mode);
        WriteOctal(header, 108, 8, 0);   // uid
        WriteOctal(header, 116, 8, 0);   // gid
        WriteOctal(header, 124, 12, size);
        WriteOctal(header, 136, 12, 0);  // mtime
        header[156] = type;
        WriteString(header, 157, 100, linkName);
        Encoding.ASCII.GetBytes("ustar\0", header.AsSpan(257, 6));
        Encoding.ASCII.GetBytes("00", header.AsSpan(263, 2));
        WriteString(header, 265, 32, "runner");
        WriteString(header, 297, 32, "runner");

        Array.Fill(header, (byte)' ', 148, 8);
        var checksum = header.Sum(static value => (int)value);
        Encoding.ASCII.GetBytes(Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ",
            header.AsSpan(148, 8));

        _output.Write(header);
        if (data is not null)
        {
            _output.Write(data);
            var padding = (HeaderSize - data.Length % HeaderSize) % HeaderSize;
            if (padding > 0)
                _output.Write(new byte[padding]);
        }

        return this;
    }

    private static void WriteString(byte[] buffer, int offset, int length, string value)
    {
        var encoded = Encoding.ASCII.GetBytes(value);
        if (encoded.Length > length)
            throw new ArgumentException($"tar field too long: {value}", nameof(value));
        encoded.CopyTo(buffer, offset);
    }

    private static void WriteOctal(byte[] buffer, int offset, int length, long value)
    {
        var text = Convert.ToString(value, 8).PadLeft(length - 1, '0');
        if (text.Length != length - 1)
            throw new ArgumentOutOfRangeException(nameof(value), value, "tar number too large");
        Encoding.ASCII.GetBytes(text, buffer.AsSpan(offset, length - 1));
        buffer[offset + length - 1] = 0;
    }
}
