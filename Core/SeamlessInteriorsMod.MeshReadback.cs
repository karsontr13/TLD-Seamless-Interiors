using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── READING A MESH THE GAME MARKED UNREADABLE ───
        //
        // Most of TLD's structure meshes have Mesh.isReadable == false: mesh.vertices comes back
        // empty and GetTriangles throws, so everything that wanted a window's real corners had to
        // settle for mesh.bounds, an axis-aligned box that is inflated by any rotation and says
        // nothing about what is inside it. The Airfield hangar's two interior side sheets are the
        // case that forced this: 104 vertices holding 17 windows of five sizes on two tiers, and
        // from the box alone they read as one leaning pane 34.4 x 9.3 m, which the window finder
        // rightly refused.
        //
        // The mesh is on the GPU regardless and Unity will hand it back. Proven live 2026-09-21,
        // used here from 2026-09-22. The positive control is that the min and max of the read
        // positions equal mesh.bounds exactly; that is asserted below and a mismatch rejects the
        // read rather than feeding a wrong window into the room.
        //
        // COST: AsyncGPUReadback.Request + WaitForCompletion is a synchronous stall of roughly a
        // frame. That is why the result is cached by the mesh's instance id: a room is read once
        // when it is built, never per frame, and two buildings sharing an interior share the read.
        //
        // See TLD-Knowledge/Reading-An-Unreadable-Mesh-From-The-GPU.md.

        private sealed class ReadMesh
        {
            internal Vector3[] Vertices;
            internal int[] Triangles;
        }

        private static readonly Dictionary<int, ReadMesh> s_ReadMeshCache = new Dictionary<int, ReadMesh>();

        // A readable mesh answers from itself; an unreadable one is read back from the GPU. Returns
        // false when neither works, and the caller then does what it did before this existed.
        internal static bool TryReadMesh(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
            vertices = null;
            triangles = null;
            if (mesh == null) return false;

            if (mesh.isReadable)
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
                return vertices != null && triangles != null && triangles.Length >= 3;
            }

            int id = mesh.GetInstanceID();
            ReadMesh cached;
            if (s_ReadMeshCache.TryGetValue(id, out cached))
            {
                if (cached == null) return false;          // a failed read is remembered too
                vertices = cached.Vertices;
                triangles = cached.Triangles;
                return true;
            }

            Vector3[] readVertices;
            int[] readTriangles;
            bool ok = TryReadMeshFromGpu(mesh, out readVertices, out readTriangles);

            s_ReadMeshCache[id] = ok ? new ReadMesh { Vertices = readVertices, Triangles = readTriangles } : null;
            if (!ok) return false;

            vertices = readVertices;
            triangles = readTriangles;
            return true;
        }

        // Dropped when a scene unloads: the ids belong to meshes that may be gone.
        internal static void ClearReadMeshCache()
        {
            s_ReadMeshCache.Clear();
        }

        private static bool TryReadMeshFromGpu(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
            vertices = null;
            triangles = null;

            GraphicsBuffer vertexBuffer = null;
            GraphicsBuffer indexBuffer = null;
            try
            {
                // POSITION IS FLOAT32X3 AT OFFSET 0 ON EVERY TLD MESH SEEN SO FAR, but the stride
                // is not: the hangar's sheet is 24 bytes (position, a half4 normal, a half2 uv).
                // Both are asked of the mesh rather than assumed.
                vertexBuffer = mesh.GetVertexBuffer(0);
                if (vertexBuffer == null) return false;

                int vertexCount = vertexBuffer.count;
                int stride = vertexBuffer.stride;
                if (vertexCount <= 0 || stride < 12) return false;

                byte[] raw = ReadBuffer(vertexBuffer, vertexCount * stride);
                if (raw == null) return false;

                var read = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    int o = i * stride;
                    read[i] = new Vector3(System.BitConverter.ToSingle(raw, o),
                                          System.BitConverter.ToSingle(raw, o + 4),
                                          System.BitConverter.ToSingle(raw, o + 8));
                }

                // POSITIVE CONTROL. The read positions must span exactly the bounds the mesh
                // already reports. If they do not, the vertex layout is not what was assumed and
                // the numbers are meaningless, so the read is refused rather than trusted.
                Vector3 min = read[0], max = read[0];
                for (int i = 1; i < vertexCount; i++) { min = Vector3.Min(min, read[i]); max = Vector3.Max(max, read[i]); }
                Bounds bounds = mesh.bounds;
                if ((min - bounds.min).sqrMagnitude > READBACK_BOUNDS_TOLERANCE_SQR
                 || (max - bounds.max).sqrMagnitude > READBACK_BOUNDS_TOLERANCE_SQR)
                {
                    MelonLogger.Msg($"[MESH-READ] '{mesh.name}': GPU'dan okunan koseler mesh.bounds ile ortusmuyor, okuma reddedildi.",
                                    $"[MESH-READ] '{mesh.name}': the corners read from the GPU do not match mesh.bounds, so the read was refused.");
                    return false;
                }

                indexBuffer = mesh.GetIndexBuffer();
                if (indexBuffer == null) return false;
                int indexCount = indexBuffer.count;
                int indexStride = indexBuffer.stride;
                if (indexCount < 3 || (indexStride != 2 && indexStride != 4)) return false;

                byte[] rawIndices = ReadBuffer(indexBuffer, indexCount * indexStride);
                if (rawIndices == null) return false;

                var tris = new int[indexCount];
                for (int i = 0; i < indexCount; i++)
                {
                    tris[i] = indexStride == 2
                        ? System.BitConverter.ToUInt16(rawIndices, i * 2)
                        : System.BitConverter.ToInt32(rawIndices, i * 4);
                    if (tris[i] < 0 || tris[i] >= vertexCount) return false;
                }

                vertices = read;
                triangles = tris;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[MESH-READ] '{mesh.name}': GPU'dan {vertexCount} kose, {indexCount / 3} ucgen okundu.",
                                    $"[MESH-READ] '{mesh.name}': {vertexCount} corners and {indexCount / 3} triangles were read from the GPU.");
                return true;
            }
            catch (System.Exception e)
            {
                MelonLogger.Msg($"[MESH-READ] '{(mesh != null ? mesh.name : "null")}' GPU'dan okunamadi: {e.Message}",
                                $"[MESH-READ] '{(mesh != null ? mesh.name : "null")}' could not be read from the GPU: {e.Message}");
                return false;
            }
            finally
            {
                if (vertexBuffer != null) vertexBuffer.Release();
                if (indexBuffer != null) indexBuffer.Release();
            }
        }

        // A readable copy of an unreadable mesh, with its normals, colours and first two texture channels,
        // and the first `subMeshes` of its parts: what is to be bent goes on this copy (Community Hall's
        // window frames, 2026-10-19). Null when the layout is not one this reads.
        internal static Mesh ReadableCopyFromGpu(Mesh mesh, int subMeshes)
        {
            if (mesh == null) return null;
            GraphicsBuffer vertexBuffer = null, indexBuffer = null;
            try
            {
                if (mesh.vertexBufferCount != 1) return null;
                vertexBuffer = mesh.GetVertexBuffer(0);
                if (vertexBuffer == null) return null;
                int count = vertexBuffer.count, stride = vertexBuffer.stride;
                byte[] raw = ReadBuffer(vertexBuffer, count * stride);
                if (raw == null) return null;

                var pos = new Vector3[count];
                Vector3[] normals = null;
                Color32[] colors = null;
                Vector2[] uv0 = null, uv1 = null;
                int offset = 0;
                foreach (var a in mesh.GetVertexAttributes())
                {
                    if (a.stream != 0) return null;
                    int size = FormatSize(a.format) * a.dimension;
                    if (size <= 0) return null;
                    int at = offset;
                    if (a.attribute == VertexAttribute.Position)
                    {
                        if (a.format != VertexAttributeFormat.Float32 || a.dimension < 3) return null;
                        for (int i = 0; i < count; i++) pos[i] = new Vector3(ReadValue(raw, i * stride + at, a.format), ReadValue(raw, i * stride + at + 4, a.format), ReadValue(raw, i * stride + at + 8, a.format));
                    }
                    else if (a.attribute == VertexAttribute.Normal && a.dimension >= 3)
                    {
                        int step = FormatSize(a.format);
                        normals = new Vector3[count];
                        for (int i = 0; i < count; i++) normals[i] = new Vector3(ReadValue(raw, i * stride + at, a.format), ReadValue(raw, i * stride + at + step, a.format), ReadValue(raw, i * stride + at + 2 * step, a.format));
                    }
                    else if (a.attribute == VertexAttribute.Color && a.format == VertexAttributeFormat.UNorm8 && a.dimension == 4)
                    {
                        colors = new Color32[count];
                        for (int i = 0; i < count; i++) { int o = i * stride + at; colors[i] = new Color32(raw[o], raw[o + 1], raw[o + 2], raw[o + 3]); }
                    }
                    else if ((a.attribute == VertexAttribute.TexCoord0 || a.attribute == VertexAttribute.TexCoord1) && a.dimension >= 2)
                    {
                        int step = FormatSize(a.format);
                        var uv = new Vector2[count];
                        for (int i = 0; i < count; i++) uv[i] = new Vector2(ReadValue(raw, i * stride + at, a.format), ReadValue(raw, i * stride + at + step, a.format));
                        if (a.attribute == VertexAttribute.TexCoord0) uv0 = uv; else uv1 = uv;
                    }
                    offset += size;
                }
                if (offset > stride) return null;

                indexBuffer = mesh.GetIndexBuffer();
                if (indexBuffer == null) return null;
                int indexStride = indexBuffer.stride;
                if (indexStride != 2 && indexStride != 4) return null;
                byte[] rawIndices = ReadBuffer(indexBuffer, indexBuffer.count * indexStride);
                if (rawIndices == null) return null;

                var copy = new Mesh { name = mesh.name + "_SI" };
                if (count > 65535) copy.indexFormat = IndexFormat.UInt32;
                copy.vertices = pos;
                if (normals != null) copy.normals = normals;
                if (colors != null) copy.colors32 = colors;
                if (uv0 != null) copy.uv = uv0;
                if (uv1 != null) copy.uv2 = uv1;
                // The parts the renderer draws, as one list: a copy with more parts than materials would draw the rest too.
                var tris = new List<int>();
                for (int s = 0; s < Mathf.Min(subMeshes, mesh.subMeshCount); s++)
                {
                    var sub = mesh.GetSubMesh(s);
                    if (sub.topology != MeshTopology.Triangles) return null;
                    for (int k = 0; k < sub.indexCount; k++)
                    {
                        int byteAt = (sub.indexStart + k) * indexStride;
                        int index = (indexStride == 2 ? System.BitConverter.ToUInt16(rawIndices, byteAt) : System.BitConverter.ToInt32(rawIndices, byteAt)) + sub.baseVertex;
                        if (index < 0 || index >= count) return null;
                        tris.Add(index);
                    }
                }
                copy.triangles = tris.ToArray();
                copy.RecalculateBounds();
                // POSITIVE CONTROL, as for a read: the copy spans the original.
                if ((copy.bounds.size - mesh.bounds.size).sqrMagnitude > READBACK_BOUNDS_TOLERANCE_SQR * 4f) { Object.Destroy(copy); return null; }
                return copy;
            }
            catch (System.Exception e)
            {
                MelonLogger.Msg($"[MESH-READ] '{mesh.name}' kopyalanamadi: {e.Message}", $"[MESH-READ] '{mesh.name}' could not be copied: {e.Message}");
                return null;
            }
            finally
            {
                if (vertexBuffer != null) vertexBuffer.Release();
                if (indexBuffer != null) indexBuffer.Release();
            }
        }

        private static int FormatSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32: case VertexAttributeFormat.UInt32: case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16: case VertexAttributeFormat.UNorm16: case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16: case VertexAttributeFormat.SInt16: return 2;
                case VertexAttributeFormat.UNorm8: case VertexAttributeFormat.SNorm8: case VertexAttributeFormat.UInt8: case VertexAttributeFormat.SInt8: return 1;
            }
            return 0;
        }

        private static float ReadValue(byte[] raw, int at, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32: return System.BitConverter.ToSingle(raw, at);
                case VertexAttributeFormat.Float16: return HalfToFloat(raw[at] | (raw[at + 1] << 8));
                case VertexAttributeFormat.UNorm8: return raw[at] / 255f;
                case VertexAttributeFormat.SNorm8: return Mathf.Max((sbyte)raw[at] / 127f, -1f);
                case VertexAttributeFormat.UNorm16: return System.BitConverter.ToUInt16(raw, at) / 65535f;
                case VertexAttributeFormat.SNorm16: return Mathf.Max(System.BitConverter.ToInt16(raw, at) / 32767f, -1f);
            }
            return 0f;
        }

        private static float HalfToFloat(int h)
        {
            int sign = (h >> 15) & 1, exponent = (h >> 10) & 31, mantissa = h & 1023;
            float value = exponent == 0 ? mantissa / 1024f * 6.103515625e-05f : (1f + mantissa / 1024f) * Mathf.Pow(2f, exponent - 15);
            return sign == 1 ? -value : value;
        }

        // One centimetre, squared: the read and the bounds agree to well under that in practice.
        private const float READBACK_BOUNDS_TOLERANCE_SQR = 0.0001f;

        private static byte[] ReadBuffer(GraphicsBuffer buffer, int wanted)
        {
            var request = AsyncGPUReadback.Request(buffer, null);
            request.WaitForCompletion();
            if (request.hasError) return null;

            int available = request.layerDataSize;
            if (available < wanted) return null;

            System.IntPtr pointer = request.GetDataRaw(0);
            if (pointer == System.IntPtr.Zero) return null;

            var bytes = new byte[wanted];
            Marshal.Copy(pointer, bytes, 0, wanted);
            return bytes;
        }
    }
}
