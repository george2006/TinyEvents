using System;
using System.Text;

namespace TinyEvents.SourceGen.Emission.Writing;

internal sealed class SourceWriter
{
    private readonly StringBuilder builder = new StringBuilder();
    private int indent;
    private bool lineStart = true;

    public void Indent()
    {
        indent++;
    }

    public void Unindent()
    {
        if (indent == 0)
        {
            throw new InvalidOperationException("Source writer indentation cannot be negative.");
        }

        indent--;
    }

    public void Write(string value)
    {
        WriteIndentIfNeeded();
        builder.Append(value);
    }

    public void WriteLine()
    {
        builder.AppendLine();
        lineStart = true;
    }

    public void WriteLine(string value)
    {
        Write(value);
        WriteLine();
    }

    public override string ToString()
    {
        return builder.ToString();
    }

    private void WriteIndentIfNeeded()
    {
        if (!lineStart)
        {
            return;
        }

        builder.Append(' ', indent * 4);
        lineStart = false;
    }
}
