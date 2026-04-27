using Godot;
using System;

namespace PLVSVLTRA.Map;

/// <summary>
/// Generates physical 3D terrain meshes from heightmap data.
/// Replaces PlaneMesh + shader displacement with real ArrayMesh geometry
/// that has correct vertex heights, normals, and collision shapes.
/// </summary>
public static class TerrainGenerator
{
    /// <summary>
    /// Generates a physical 3D ArrayMesh from heightmap data and applies it
    /// to the given MeshInstance3D. Also generates a trimesh collision shape.
    /// Returns the generated ArrayMesh so it can be applied to clones.
    /// </summary>
    /// <param name="meshInstance">The MeshInstance3D to receive the new mesh.</param>
    /// <param name="heightMap">Height map image (grayscale, 0=sea level, 1=max height).</param>
    /// <param name="waterMap">Water mask image (values > 0.16 = water).</param>
    /// <param name="meshSize">Desired XZ size of the terrain mesh.</param>
    /// <param name="uvMin">World UV minimum (country/state bounds).</param>
    /// <param name="uvMax">World UV maximum (country/state bounds).</param>
    /// <param name="heightScale">Maximum height in world units.</param>
    /// <param name="collisionShape">The CollisionShape3D node to update, or null.</param>
    /// <param name="resolution">Grid resolution (vertices per axis). Default 512.</param>
    public static ArrayMesh Generate(
        MeshInstance3D meshInstance,
        Image heightMap,
        Image waterMap,
        Vector2 meshSize,
        Vector2 uvMin,
        Vector2 uvMax,
        float heightScale,
        CollisionShape3D collisionShape = null,
        int resolution = 512)
    {
        if (heightMap == null)
        {
            GD.PushError("[TerrainGenerator] Height map is null, cannot generate terrain.");
            return null;
        }

        var timer = new System.Diagnostics.Stopwatch();
        timer.Start();

        // Preserve the existing ShaderMaterial before replacing the mesh
        ShaderMaterial existingMaterial = null;
        if (meshInstance != null)
        {
            existingMaterial = meshInstance.GetActiveMaterial(0) as ShaderMaterial;
            if (existingMaterial == null && meshInstance.Mesh != null)
                existingMaterial = meshInstance.Mesh.SurfaceGetMaterial(0) as ShaderMaterial;
        }

        int resX = resolution;
        int resZ = resolution;
        int vertexCount = resX * resZ;

        float halfW = meshSize.X / 2f;
        float halfH = meshSize.Y / 2f;

        int hmW = heightMap.GetWidth();
        int hmH = heightMap.GetHeight();
        int wmW = waterMap?.GetWidth() ?? 0;
        int wmH = waterMap?.GetHeight() ?? 0;

        // Pre-allocate arrays
        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        float[] heights = new float[vertexCount]; // cache for normal calculation

        // === Pass 1: Generate vertices and UVs ===
        for (int z = 0; z < resZ; z++)
        {
            float tz = (float)z / (resZ - 1);
            float posZ = -halfH + tz * meshSize.Y;

            for (int x = 0; x < resX; x++)
            {
                float tx = (float)x / (resX - 1);
                float posX = -halfW + tx * meshSize.X;

                // Local UV (0-1 range for shader)
                Vector2 localUV = new Vector2(tx, tz);

                // World UV (mapped to country/state bounds)
                float worldU = Mathf.Lerp(uvMin.X, uvMax.X, tx);
                float worldV = Mathf.Lerp(uvMin.Y, uvMax.Y, tz);

                // Sample heightmap
                int hpx = Mathf.Clamp((int)(worldU * hmW), 0, hmW - 1);
                int hpy = Mathf.Clamp((int)(worldV * hmH), 0, hmH - 1);
                float h = heightMap.GetPixel(hpx, hpy).R;

                // Sample water map — flatten water areas to sea level
                float waterValue = 0f;
                if (waterMap != null)
                {
                    int wpx = Mathf.Clamp((int)(worldU * wmW), 0, wmW - 1);
                    int wpy = Mathf.Clamp((int)(worldV * wmH), 0, wmH - 1);
                    waterValue = waterMap.GetPixel(wpx, wpy).R;
                }

                // Smooth coastline transition instead of hard cutoff
                // Ocean surface sits at Y=1.0 (from ocean mesh center_offset)
                const float oceanY = 1.0f;
                const float landBase = 0.3f;   // land starts this much above ocean
                const float waterDepth = 0.5f; // water verts sit this far below ocean

                // Gradual water blend: 0=land, 1=deep water
                float waterBlend;
                if (waterValue > 0.25f)
                    waterBlend = 1f;
                else if (waterValue < 0.05f)
                    waterBlend = 0f;
                else
                    waterBlend = (waterValue - 0.05f) / 0.20f;

                float landHeight = oceanY + landBase + h * heightScale;
                float waterHeight = oceanY - waterDepth;
                float finalHeight = Mathf.Lerp(landHeight, waterHeight, waterBlend);

                int idx = z * resX + x;
                vertices[idx] = new Vector3(posX, finalHeight, posZ);
                uvs[idx] = localUV;
                heights[idx] = finalHeight;
            }
        }

        // === Pass 2: Calculate normals using finite differences ===
        for (int z = 0; z < resZ; z++)
        {
            for (int x = 0; x < resX; x++)
            {
                int idx = z * resX + x;

                // Get neighboring heights
                float hL = (x > 0) ? heights[z * resX + (x - 1)] : heights[idx];
                float hR = (x < resX - 1) ? heights[z * resX + (x + 1)] : heights[idx];
                float hD = (z > 0) ? heights[(z - 1) * resX + x] : heights[idx];
                float hU = (z < resZ - 1) ? heights[(z + 1) * resX + x] : heights[idx];

                // Grid spacing in world units
                float dx = meshSize.X / (resX - 1);
                float dz = meshSize.Y / (resZ - 1);

                // Central difference normal
                Vector3 normal = new Vector3(
                    (hL - hR) / (2f * dx),
                    1f,
                    (hD - hU) / (2f * dz)
                ).Normalized();

                normals[idx] = normal;
            }
        }

        // === Pass 3: Generate triangle indices (clockwise winding for cull_back) ===
        int quadCount = (resX - 1) * (resZ - 1);
        int[] indices = new int[quadCount * 6];
        int triIdx = 0;

        for (int z = 0; z < resZ - 1; z++)
        {
            for (int x = 0; x < resX - 1; x++)
            {
                int topLeft = z * resX + x;
                int topRight = z * resX + (x + 1);
                int bottomLeft = (z + 1) * resX + x;
                int bottomRight = (z + 1) * resX + (x + 1);

                // Triangle 1 (clockwise when viewed from above: top-left, top-right, bottom-left)
                indices[triIdx++] = topLeft;
                indices[triIdx++] = topRight;
                indices[triIdx++] = bottomLeft;

                // Triangle 2 (clockwise: top-right, bottom-right, bottom-left)
                indices[triIdx++] = topRight;
                indices[triIdx++] = bottomRight;
                indices[triIdx++] = bottomLeft;
            }
        }

        // === Build ArrayMesh ===
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // Re-apply the original ShaderMaterial
        if (existingMaterial != null)
        {
            arrayMesh.SurfaceSetMaterial(0, existingMaterial);
        }

        // Apply to MeshInstance3D
        if (meshInstance != null)
        {
            meshInstance.Mesh = arrayMesh;
        }

        // === Generate collision shape ===
        if (collisionShape != null)
        {
            // Build face array for ConcavePolygonShape3D
            Vector3[] faces = new Vector3[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                faces[i] = vertices[indices[i]];
            }
            var trimesh = new ConcavePolygonShape3D();
            trimesh.SetFaces(faces);
            collisionShape.Shape = trimesh;
            // Position collision at same height as the mesh
            collisionShape.Position = meshInstance?.Position ?? Vector3.Zero;
        }

        timer.Stop();
        GD.Print($"[TerrainGenerator] Generated {resX}×{resZ} terrain mesh " +
                 $"({vertexCount} verts, {quadCount * 2} tris) in {timer.ElapsedMilliseconds}ms");

        return arrayMesh;
    }
}
