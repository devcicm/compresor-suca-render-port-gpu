using System.Diagnostics;
using System.Text.Json;

namespace SucaGpuLab;

internal enum TextureFormat { Rgba8, Bc1, Bc3, Bc7, Astc4x4 }

internal readonly record struct TextureDescriptor(int Width, int Height, int MipLevels, TextureFormat Format)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || MipLevels <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        int maxLevels = 1 + (int)Math.Floor(Math.Log2(Math.Max(Width, Height)));
        if (MipLevels > maxLevels) throw new ArgumentOutOfRangeException(nameof(MipLevels), "Mip levels exceed texture dimensions.");
    }
}

internal static class TextureLayout
{
    // Logical GPU payload estimate; excludes driver allocation padding, alignment and metadata.
    public static long Bytes(TextureDescriptor descriptor)
    {
        descriptor.Validate();
        long total = 0;
        for (int level = 0; level < descriptor.MipLevels; level++)
        {
            int w = Math.Max(1, descriptor.Width >> level);
            int h = Math.Max(1, descriptor.Height >> level);
            total = checked(total + descriptor.Format switch
            {
                TextureFormat.Rgba8 => checked((long)w * h * 4),
                TextureFormat.Bc1 => checked(((long)(w + 3) / 4) * ((long)(h + 3) / 4) * 8),
                TextureFormat.Bc3 or TextureFormat.Bc7 or TextureFormat.Astc4x4
                    => checked(((long)(w + 3) / 4) * ((long)(h + 3) / 4) * 16),
                _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
            });
        }
        return total;
    }
}

internal readonly record struct ImageSignals(double MeanLuma, double EntropyBits, double EdgeDensity);

internal static class SucaSignals
{
    // A deliberately small, deterministic CPU baseline; NOT the original SUCA kernel.
    public static ImageSignals Analyze(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Expected tightly packed RGBA8 pixels.");
        Span<int> histogram = stackalloc int[256];
        long sum = 0;
        long edgeCount = 0;
        int n = checked(width * height);
        byte[] luma = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int p = i * 4;
            byte y = (byte)Math.Clamp((int)Math.Round(0.299 * rgba[p] + 0.587 * rgba[p + 1] + 0.114 * rgba[p + 2]), 0, 255);
            luma[i] = y;
            histogram[y]++;
            sum += y;
        }
        double entropy = 0;
        foreach (int count in histogram)
        {
            if (count == 0) continue;
            double probability = (double)count / n;
            entropy -= probability * Math.Log2(probability);
        }
        // Neighbor-difference proxy, NOT SUCA's Sobel edge detector.
        long comparisons = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            if (x > 0) { comparisons++; if (Math.Abs(luma[index] - luma[index - 1]) > 80) edgeCount++; }
            if (y > 0) { comparisons++; if (Math.Abs(luma[index] - luma[index - width]) > 80) edgeCount++; }
        }
        return new ImageSignals((double)sum / n, entropy, comparisons == 0 ? 0 : (double)edgeCount / comparisons);
    }
}

internal static class Fixtures
{
    public static byte[] Checker(int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            byte v = ((x / 8 + y / 8) & 1) == 0 ? (byte)32 : (byte)224;
            pixels[p] = v;
            pixels[p + 1] = v;
            pixels[p + 2] = v;
            pixels[p + 3] = 255;
        }
        return pixels;
    }
}

internal static class Program
{
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + description);
        Console.WriteLine("PASS " + description);
    }

    private static void SelfTest()
    {
        Check(TextureLayout.Bytes(new(4, 4, 1, TextureFormat.Rgba8)) == 64, "RGBA8 4x4 = 64 bytes");
        Check(TextureLayout.Bytes(new(4, 4, 1, TextureFormat.Bc1)) == 8, "BC1 4x4 = 8 bytes");
        Check(TextureLayout.Bytes(new(4, 4, 1, TextureFormat.Bc7)) == 16, "BC7 4x4 = 16 bytes");
        Check(TextureLayout.Bytes(new(5, 5, 1, TextureFormat.Bc7)) == 64, "BC7 partial blocks rounded up");
        Check(TextureLayout.Bytes(new(4, 4, 3, TextureFormat.Rgba8)) == 84, "RGBA8 mip chain 4x4+2x2+1x1");
        Check(TextureLayout.Bytes(new(4, 4, 3, TextureFormat.Bc7)) == 48, "BC7 minimum block per mip");
        byte[] checker = Fixtures.Checker(32, 32);
        ImageSignals signals = SucaSignals.Analyze(checker, 32, 32);
        Check(signals.EntropyBits > 0.9 && signals.EntropyBits < 1.1, "Checker entropy approximately 1 bit");
        Check(signals.MeanLuma == 128, "Checker mean luminance = 128");
        Check(signals.EdgeDensity > 0 && signals.EdgeDensity < 1, "Checker edges detected");
        bool rejected = false;
        try { _ = TextureLayout.Bytes(new(0, 4, 1, TextureFormat.Rgba8)); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid dimensions rejected");
        Console.WriteLine("All self-tests passed.");
    }

    private static void Benchmark(int size, int iterations)
    {
        if (size < 8 || size > 4096 || iterations < 1 || iterations > 10000)
            throw new ArgumentOutOfRangeException(nameof(size), "size must be 8..4096 and iterations 1..10000");
        byte[] rgba = Fixtures.Checker(size, size);
        _ = SucaSignals.Analyze(rgba, size, size); // warmup
        double[] elapsed = new double[iterations];
        ImageSignals result = default;
        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            result = SucaSignals.Analyze(rgba, size, size);
            elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(elapsed);
        double Percentile(double p) => elapsed[Math.Clamp((int)Math.Ceiling(p * elapsed.Length) - 1, 0, elapsed.Length - 1)];
        var output = new
        {
            mode = "cpu-synthetic-baseline",
            host = new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), cpuCount = Environment.ProcessorCount },
            input = new { width = size, height = size, iterations },
            signals = result,
            timingMs = new { p50 = Percentile(.50), p95 = Percentile(.95), p99 = Percentile(.99) },
            estimatedPayloadBytes = new
            {
                rgba8 = TextureLayout.Bytes(new(size, size, 1, TextureFormat.Rgba8)),
                bc1 = TextureLayout.Bytes(new(size, size, 1, TextureFormat.Bc1)),
                bc7 = TextureLayout.Bytes(new(size, size, 1, TextureFormat.Bc7))
            },
            caveat = "No GPU access, GPU driver probe, texture encoding, VRAM measurement, or nxnative integration."
        };
        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "selftest") SelfTest();
            else if (args.Length >= 1 && args[0] == "bench")
                Benchmark(args.Length >= 2 ? int.Parse(args[1]) : 256, args.Length >= 3 ? int.Parse(args[2]) : 20);
            else
            {
                Console.WriteLine("SUCA GPU Lab: CPU-only reference, not a GPU port.");
                Console.WriteLine("Usage: dotnet run --project src/SucaGpuLab -- selftest");
                Console.WriteLine("       dotnet run --project src/SucaGpuLab -c Release -- bench [size=256] [iterations=20]");
                return 1;
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
    }
}
