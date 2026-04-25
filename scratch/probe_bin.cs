using System;
using System.IO;

public class Probe
{
    public static void Main()
    {
        string path = "/run/media/toni/6EF68777F6873DF9/LINUX/CODE/ProjectPLVSVLTRA/data/map/world_data.bin";
        if (!File.Exists(path)) {
            Console.WriteLine("File not found: " + path);
            return;
        }
        
        byte[] data = File.ReadAllBytes(path);
        Console.WriteLine($"Size: {data.Length} bytes");
        
        // Try different strides
        for (int stride = 1; stride <= 32; stride++)
        {
            if (data.Length % stride == 0)
            {
                Console.WriteLine($"Possible stride: {stride} (Total elements: {data.Length / stride})");
            }
        }
        
        // Read first 10 elements with 20-byte stride (my guess)
        Console.WriteLine("\nFirst 10 elements (20-byte stride):");
        using (var ms1 = new MemoryStream(data))
        using (var br1 = new BinaryReader(ms1))
        {
            for (int i = 0; i < 10; i++)
            {
                if (ms1.Position + 20 > data.Length) break;
                Console.Write($"[{i}] ");
                Console.Write($"S:{br1.ReadUInt16()} C:{br1.ReadUInt16()} ");
                Console.Write($"F1:{br1.ReadSingle()} F2:{br1.ReadSingle()} F3:{br1.ReadSingle()} F4:{br1.ReadSingle()}");
                Console.WriteLine();
            }
        }
        
        // Read first 10 elements with 4-byte stride (classic)
        Console.WriteLine("\nFirst 10 elements (4-byte stride):");
        using (var ms2 = new MemoryStream(data))
        using (var br2 = new BinaryReader(ms2))
        {
            for (int i = 0; i < 10; i++)
            {
                if (ms2.Position + 4 > data.Length) break;
                Console.WriteLine($"[{i}] S:{br2.ReadUInt16()} C:{br2.ReadUInt16()}");
            }
        }
    }
}
