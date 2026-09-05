using System.Globalization;

namespace Baudacious.ConPtySpike;

/// <summary>
/// Annotated view of a capture: what ConPTY actually sent, split into VT tokens, with
/// the arrival time of the read each token came from.
/// </summary>
internal static class Dump
{
    public static int Run(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: dump <capture.bin> [-n <max tokens>] [--hex]");
            return 1;
        }

        var binPath = args[0];
        if (!File.Exists(binPath))
        {
            Console.Error.WriteLine($"no such capture: {binPath}");
            return 1;
        }

        var max = int.MaxValue;
        var hex = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--hex")
            {
                hex = true;
            }
            else if (args[i] == "-n" && i + 1 < args.Length
                     && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var parsed))
            {
                max = parsed;
                i++;
            }
        }

        var bytes = File.ReadAllBytes(binPath);
        var arrivals = ReadTiming(Path.ChangeExtension(binPath, ".timing"));

        Console.WriteLine($"# {binPath}");
        Console.WriteLine($"# {bytes.Length} bytes, {arrivals.Count} reads");
        Console.WriteLine();

        if (hex)
        {
            WriteHex(bytes, max);
            return 0;
        }

        var tokens = Vt.Tokenize(bytes);
        Console.WriteLine($"# {tokens.Count} VT tokens");
        Console.WriteLine();
        Console.WriteLine($"{"offset",8}  {"at (ms)",9}  {"kind",-8} {"len",4}  content");

        var shown = 0;
        foreach (var token in tokens)
        {
            if (shown++ >= max)
            {
                Console.WriteLine($"... {tokens.Count - max} more tokens");
                break;
            }

            var at = ArrivalOf(arrivals, token.Offset);
            var atText = at is null ? "" : (at.Value / 1000.0).ToString("F1", CultureInfo.InvariantCulture);
            var text = token.Text.Replace("\r", "\\r", StringComparison.Ordinal)
                                 .Replace("\n", "\\n", StringComparison.Ordinal);
            if (text.Length > 90)
            {
                text = text[..90] + "…";
            }

            Console.WriteLine($"{token.Offset,8}  {atText,9}  {token.Kind,-8} {token.Length,4}  {text}");
        }

        Console.WriteLine();
        foreach (var group in tokens.GroupBy(t => t.Kind).OrderByDescending(g => g.Sum(t => t.Length)))
        {
            var total = group.Sum(t => t.Length);
            Console.WriteLine($"  {group.Key,-8} {group.Count(),6} tokens {total,8} bytes  {100.0 * total / bytes.Length,5:F1}%");
        }

        return 0;
    }

    /// <summary>Reads the (micros, offset, length) records written alongside the capture.</summary>
    private static List<(long Micros, long Offset)> ReadTiming(string path)
    {
        var list = new List<(long, long)>();
        if (!File.Exists(path))
        {
            return list;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ');
            if (parts.Length >= 2
                && long.TryParse(parts[0], CultureInfo.InvariantCulture, out var micros)
                && long.TryParse(parts[1], CultureInfo.InvariantCulture, out var offset))
            {
                list.Add((micros, offset));
            }
        }

        return list;
    }

    /// <summary>Arrival time of the read that delivered the byte at <paramref name="offset"/>.</summary>
    private static long? ArrivalOf(List<(long Micros, long Offset)> arrivals, int offset)
    {
        if (arrivals.Count == 0)
        {
            return null;
        }

        var lo = 0;
        var hi = arrivals.Count - 1;
        long? best = null;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (arrivals[mid].Offset <= offset)
            {
                best = arrivals[mid].Micros;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }

    private static void WriteHex(byte[] bytes, int maxLines)
    {
        for (var i = 0; i < bytes.Length && i / 16 < maxLines; i += 16)
        {
            var run = Math.Min(16, bytes.Length - i);
            var hex = string.Join(' ', bytes.Skip(i).Take(run).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            var text = string.Concat(bytes.Skip(i).Take(run).Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.'));
            Console.WriteLine($"{i:x8}  {hex,-47}  {text}");
        }
    }
}
