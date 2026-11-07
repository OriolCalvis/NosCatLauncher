using System.Text;

namespace NosCatLauncher.Core;

/// <summary>Splits a byte stream into top-level JSON objects (the pipe carries no length prefix).</summary>
public sealed class JsonFramer
{
    private readonly List<byte> _buffer = [];

    public void Append(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            _buffer.Add(b);
        }
    }

    public bool TryRead(out string message)
    {
        message = "";
        var start = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < _buffer.Count; i++)
        {
            var b = _buffer[i];
            if (start < 0)
            {
                if (b == '{')
                {
                    start = i;
                    depth = 1;
                }

                continue;
            }

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (b == '\\')
                {
                    escaped = true;
                }
                else if (b == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (b)
            {
                case (byte)'"':
                    inString = true;
                    break;
                case (byte)'{':
                    depth++;
                    break;
                case (byte)'}' when --depth == 0:
                    message = Encoding.UTF8.GetString(_buffer.GetRange(start, i - start + 1).ToArray());
                    _buffer.RemoveRange(0, i + 1);
                    return true;
            }
        }

        if (start < 0)
        {
            _buffer.Clear();
        }
        else if (start > 0)
        {
            _buffer.RemoveRange(0, start);
        }

        return false;
    }
}
