using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Compresses the rooms' pictures (tools/interiors/render-rooms.py's rooms-night.png and
/// rooms-day.png) into what the mod ships: raw DXT1 with every mip, behind a small header
/// (Glass/RoomAtlas.cs reads it). Run headless by tools/interiors/build-rooms.ps1:
///   Unity.exe -batchmode -nographics -quit -projectPath UnityProject -executeMethod RoomAtlasBaker.Bake
/// with RG_ROOMS_IN (the folder of the two PNGs and rooms-pools.txt) and RG_ROOMS_OUT (where the
/// .bytes go). The mips are box-filtered: the tiles are powers of two on a power-of-two grid, so no
/// level up to a tile's last (1 x 1, the room's average) mixes two rooms.
/// </summary>
public static class RoomAtlasBaker
{
    private const string Folder = "Assets/RoomBake";
    private const int Version = 1;
    private const int PoolCount = 8;

    public static void Bake()
    {
        try
        {
            string input = Environment.GetEnvironmentVariable("RG_ROOMS_IN");
            string output = Environment.GetEnvironmentVariable("RG_ROOMS_OUT");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                throw new Exception("RG_ROOMS_IN and RG_ROOMS_OUT must be set");

            string[] pools = File.ReadAllLines(Path.Combine(input, "rooms-pools.txt"));
            string[] head = pools[0].Split(' ');
            int perRow = int.Parse(head[0]), nightTile = int.Parse(head[1]), dayTile = int.Parse(head[2]), tiles = int.Parse(head[3]);
            var first = new int[PoolCount];
            var count = new int[PoolCount];
            for (int k = 0; k < PoolCount; k++)
            {
                string[] p = pools[k + 1].Split(' ');
                first[k] = int.Parse(p[1]);
                count[k] = int.Parse(p[2]);
            }

            Directory.CreateDirectory(Folder);
            One(Path.Combine(input, "rooms-night.png"), Path.Combine(output, "rooms-night.bytes"), perRow, nightTile, tiles, first, count);
            One(Path.Combine(input, "rooms-day.png"), Path.Combine(output, "rooms-day.bytes"), perRow, dayTile, tiles, first, count);
            AssetDatabase.DeleteAsset(Folder);
            Debug.Log("ROOMS BAKE OK");
        }
        catch (Exception e)
        {
            Debug.LogError("ROOMS BAKE FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static void One(string png, string bytes, int perRow, int tilePx, int tiles, int[] first, int[] count)
    {
        string asset = Folder + "/" + Path.GetFileName(png);
        File.Copy(png, asset, true);
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(asset);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Standalone",
            overridden = true,
            maxTextureSize = 4096,
            format = TextureImporterFormat.DXT1,
            compressionQuality = 100,
        });
        importer.SaveAndReimport();

        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
        if (t == null || t.format != TextureFormat.DXT1)
            throw new Exception(png + ": imported as " + (t == null ? "nothing" : t.format.ToString()) + ", not DXT1");
        byte[] data = t.GetRawTextureData();

        using (var w = new BinaryWriter(File.Create(bytes)))
        {
            w.Write(new[] { (byte)'R', (byte)'G', (byte)'R', (byte)'A' });
            w.Write(Version);
            w.Write(t.width);
            w.Write(t.height);
            w.Write(t.mipmapCount);
            w.Write((int)TextureFormat.DXT1);
            w.Write(perRow);
            w.Write(tilePx);
            w.Write(tiles);
            w.Write(PoolCount);
            for (int k = 0; k < PoolCount; k++)
            {
                w.Write(first[k]);
                w.Write(count[k]);
            }
            w.Write(data.Length);
            w.Write(data);
        }
        Debug.Log("ROOMS " + Path.GetFileName(bytes) + ": " + t.width + "x" + t.height + ", " + t.mipmapCount + " mips, " + data.Length + " bytes");
    }
}
