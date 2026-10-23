using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// GPU PREVIEW ONLY: real vehicle assets for the preview's streets, read straight from Workshop .crp
/// packages on this machine (never shipped, never copied into the repo): the main mesh, its _MainTex /
/// _XYSMap / _ACIMap (DXT, uploaded as stored with LoadRawTextureData -- the package keeps Unity's own
/// raw data, row 0 at v = 0) and the material's _FloorParams.
///
/// The package: "CRAP", ushort version, package, author, uint, main asset, int count, long data
/// offset; per entry name, checksum, int type (1 data, 2 material, 3 texture, 4 mesh), long offset,
/// long size. A texture: bool, type name, texture name (_MainTex... ; an LOD's are unnamed), bool
/// linear, [int aniso on newer packages], int length, a DDS file. A mesh: bool, type name, name, then
/// int-counted arrays: vertices, colours (16 B), uv, normals, tangents, bone weights (32 B), bind poses
/// (64 B), int sub-meshes, each int count + indices. (docs/crpsurvey.ps1 reads the same.)
/// </summary>
public sealed class PreviewVehicleAsset
{
    public string Name;
    public Mesh Mesh;
    public Texture2D Main, Xys, Aci;
    public Vector4 Floor = new Vector4(0.5f, 2f, 2.5f, 5f);

    public static PreviewVehicleAsset Load(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        {
            var r = new BinaryReader(fs);
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "CRAP")
                throw new Exception(path + " is not a .crp");
            r.ReadUInt16();
            r.ReadString();
            r.ReadString();
            r.ReadUInt32();
            var a = new PreviewVehicleAsset { Name = r.ReadString() };
            int count = r.ReadInt32();
            long dataOff = r.ReadInt64();
            var types = new int[count];
            var names = new string[count];
            var offs = new long[count];
            var sizes = new long[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = r.ReadString();
                r.ReadString();
                types[i] = r.ReadInt32();
                offs[i] = r.ReadInt64();
                sizes[i] = r.ReadInt64();
            }

            for (int i = 0; i < count; i++)
            {
                fs.Position = dataOff + offs[i];
                byte[] b = r.ReadBytes((int)sizes[i]);
                if (types[i] == 3)
                    ReadTexture(a, b);
                else if (types[i] == 4 && a.Mesh == null && names[i].IndexOf("lod", StringComparison.OrdinalIgnoreCase) < 0)
                    a.Mesh = ReadMesh(b);
                else if (types[i] == 2)
                    ReadFloor(a, b);
            }
            if (a.Mesh == null || a.Main == null || a.Xys == null || a.Aci == null)
                throw new Exception(path + ": no main mesh or no _MainTex/_XYSMap/_ACIMap");
            return a;
        }
    }

    private static void ReadTexture(PreviewVehicleAsset a, byte[] b)
    {
        var r = new BinaryReader(new MemoryStream(b));
        r.ReadBoolean();
        r.ReadString();
        string name = r.ReadString();
        bool wanted = (name == "_MainTex" && a.Main == null) || (name == "_XYSMap" && a.Xys == null) || (name == "_ACIMap" && a.Aci == null);
        if (!wanted)
            return;
        bool linear = r.ReadBoolean();
        long p = r.BaseStream.Position;
        int len = r.ReadInt32();
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "DDS ")
        {
            r.BaseStream.Position = p + 4;   // a newer package: the aniso level came first
            len = r.ReadInt32();
        }
        else
            r.BaseStream.Position = p + 4;
        Texture2D t = FromDds(r.ReadBytes(len), linear || name != "_MainTex");
        t.name = a.Name + name;
        if (name == "_MainTex") a.Main = t;
        else if (name == "_XYSMap") a.Xys = t;
        else a.Aci = t;
    }

    private static Texture2D FromDds(byte[] dds, bool linear)
    {
        int h = BitConverter.ToInt32(dds, 12), w = BitConverter.ToInt32(dds, 16);
        int mips = Math.Max(1, BitConverter.ToInt32(dds, 28));
        string four = Encoding.ASCII.GetString(dds, 84, 4);
        TextureFormat fmt;
        int block;
        if (four == "DXT1") { fmt = TextureFormat.DXT1; block = 8; }
        else if (four == "DXT5") { fmt = TextureFormat.DXT5; block = 16; }
        else throw new Exception("texture format " + four + " not handled by the preview");

        int full = 1;
        for (int s = Math.Max(w, h); s > 1; s >>= 1) full++;
        bool chain = mips >= full;
        long bytes = 0;
        for (int m = 0, mw = w, mh = h; m < (chain ? full : 1); m++, mw = Math.Max(1, mw / 2), mh = Math.Max(1, mh / 2))
            bytes += ((mw + 3) / 4) * ((mh + 3) / 4) * block;
        var data = new byte[bytes];
        Buffer.BlockCopy(dds, 128, data, 0, (int)Math.Min(bytes, dds.Length - 128));
        var t = new Texture2D(w, h, fmt, chain, linear);
        t.LoadRawTextureData(data);
        t.Apply(false, false);
        t.wrapMode = TextureWrapMode.Repeat;
        t.filterMode = FilterMode.Trilinear;
        t.anisoLevel = 4;
        return t;
    }

    private static Mesh ReadMesh(byte[] b)
    {
        var r = new BinaryReader(new MemoryStream(b));
        r.ReadBoolean();
        r.ReadString();
        r.ReadString();
        var verts = new Vector3[r.ReadInt32()];
        for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var colours = new Color[r.ReadInt32()];
        for (int i = 0; i < colours.Length; i++) colours[i] = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var uv = new Vector2[r.ReadInt32()];
        for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        var normals = new Vector3[r.ReadInt32()];
        for (int i = 0; i < normals.Length; i++) normals[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var tangents = new Vector4[r.ReadInt32()];
        for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        // (Each count read before the stream is moved: "Position += ReadInt32()" takes Position first.)
        long boneWeights = r.ReadInt32();
        r.BaseStream.Position += boneWeights * 32L;
        long bindPoses = r.ReadInt32();
        r.BaseStream.Position += bindPoses * 64L;
        int subs = r.ReadInt32();
        var tris = new List<int>();
        for (int s = 0; s < subs; s++)
        {
            int c = r.ReadInt32();
            for (int i = 0; i < c; i++) tris.Add(r.ReadInt32());
        }

        var m = new Mesh();
        m.vertices = verts;
        if (normals.Length == verts.Length) m.normals = normals;
        if (uv.Length == verts.Length) m.uv = uv;
        if (tangents.Length == verts.Length) m.tangents = tangents;
        if (colours.Length == verts.Length) m.colors = colours;
        m.SetTriangles(tris, 0);
        if (normals.Length != verts.Length) m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // The material's _FloorParams: its name, then its four floats.
    private static void ReadFloor(PreviewVehicleAsset a, byte[] b)
    {
        byte[] key = Encoding.ASCII.GetBytes("_FloorParams");
        for (int i = 0; i + key.Length + 16 <= b.Length; i++)
        {
            int j = 0;
            while (j < key.Length && b[i + j] == key[j]) j++;
            if (j < key.Length)
                continue;
            int p = i + key.Length;
            var f = new Vector4(BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8), BitConverter.ToSingle(b, p + 12));
            if (f.y > 0f && f.z > 0f && f.w > 0f && f.y < 50f)
                a.Floor = f;
            return;
        }
    }
}
