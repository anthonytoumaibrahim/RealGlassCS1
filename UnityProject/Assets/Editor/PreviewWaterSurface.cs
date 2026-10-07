using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// The older optical tests use analytic planes. Give them an explicit surface buffer, too: no
// production fallback is permitted to infer water from the seabed. WaterSurfaceTest exercises
// the game's simulated meshes and the production capture class separately.
public static class PreviewWaterSurface
{
    public static void Record(CommandBuffer buffer, Material reflection, GameObject water, int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        target.filterMode = FilterMode.Point;
        target.Create();
        var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/PreviewWaterSurface.shader"));
        reflection.SetTexture("_RGWaterSurfaceDepth", target);
        buffer.SetRenderTarget(target);
        buffer.ClearRenderTarget(true, true, Color.clear);
        buffer.DrawMesh(water.GetComponent<MeshFilter>().sharedMesh, water.transform.localToWorldMatrix, material, 0, 0);
        buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
    }
}
