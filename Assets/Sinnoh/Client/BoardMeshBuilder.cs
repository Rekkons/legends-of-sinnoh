using System.Collections.Generic;
using System.Globalization;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sinnoh.Client
{
    /// <summary>
    /// 메시 묶음 = 서브메시 = 그리기 호출(패스마다). 윗면은 디퓨즈별(TileDiffuse 순서 그대로), 옆면은 하나(틴트만, 텍스처 없음 — d3-3 §2-1).
    /// Water*는 바다·호수 반투명 비교(splitWater)일 때만 쓴다.
    /// </summary>
    public enum HexGroup { Standard, ForestCold, ForestTemperate, ForestWarm, Secret, Sides, WaterTop, WaterSides }

    /// <summary>칸 하나가 합친 메시 안 어디 있나(시험·캡처가 읽는다). 윗면 정점 7개(가운데 + 테두리 6, 테두리 k = 60k°), 옆면은 띠마다 24(면 6 × 4).</summary>
    public readonly struct CellSpan
    {
        public readonly int Part;
        public readonly int TopStart;
        public readonly int SideStart;
        public readonly int Bands;
        public readonly HexGroup TopGroup;

        public CellSpan(int part, int topStart, int sideStart, int bands, HexGroup topGroup)
        {
            Part = part;
            TopStart = topStart;
            SideStart = sideStart;
            Bands = bands;
            TopGroup = topGroup;
        }
    }

    /// <summary>
    /// board.json 351칸 → 헥스 프리즘 메시 둘(0 본맵 326칸, 1 깨어진 세계 25칸). 모양은 BoardLayout(외접 1.1, 층 0.78, 기둥 틴트 띠).
    /// 칸마다 정점을 따로 두고(면마다 평평한 법선) 한 메시로 합친다 — 그리기 호출은 메시마다 쓰는 묶음 수(본맵 5 = 디퓨즈 4 + 옆면 1).
    /// 정점색 = 틴트 바이트 그대로(sRGB, 셰이더가 선형으로), uv0 = 원작 OBJ 윗면 UV, uv1.x = 윗면 테두리 정도(가운데 0 → 테두리 1).
    /// 밑면은 만들지 않는다(기울기 55° 카메라에서 안 보인다).
    /// </summary>
    public static class BoardMeshBuilder
    {
        public const int Parts = 2;
        public const int TopVerts = 7;
        public const int SideVertsPerBand = 24;

        // 원작 헥스 OBJ(pastebin PMjCsZFs)의 윗면 UV: 텍스처 오른쪽 위 4분면(숲 디퓨즈의 그림이 거기 있다) [확인 OBJ vt 23~28].
        // u = U0 + UX·(dx/R), v = V0 − VZ·(dz/R)
        const float U0 = 0.764191f, UX = 0.229464f, V0 = 0.797145f, VZ = 0.229330f;

        public sealed class Part
        {
            public Mesh Mesh;
            /// <summary>서브메시 i의 묶음.</summary>
            public HexGroup[] Groups;
        }

        public static Part[] Build(Board board, IReadOnlyList<CellLook> looks, bool splitWater, out CellSpan[] spans)
        {
            var acc = new Acc[Parts];
            for (int p = 0; p < Parts; p++) acc[p] = new Acc();
            spans = new CellSpan[board.Count];
            float r = (float)BoardLayout.HexRadius;
            var rim = new Vector2[6];
            var sideNormal = new Vector3[6];
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad, n = (k * 60f + 30f) * Mathf.Deg2Rad;
                rim[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                sideNormal[k] = new Vector3(Mathf.Cos(n), 0f, Mathf.Sin(n));
            }

            for (int i = 0; i < board.Count; i++)
            {
                var cell = board[i];
                var look = looks[i];
                var bands = BoardLayout.Bands(cell, look);
                int part = cell.DistortionWorld ? 1 : 0;
                var a = acc[part];
                float cx = (float)look.X, cz = (float)look.Z, top = (float)bands[bands.Length - 1].Top;
                var cover = ParseTint(look.Tint);
                bool water = splitWater && cover.a < 255 && look.Diffuse == TileDiffuse.Standard;
                var topGroup = water ? HexGroup.WaterTop : (HexGroup)(int)look.Diffuse;
                var sideGroup = water ? HexGroup.WaterSides : HexGroup.Sides;

                // 윗면: 가운데 + 테두리 6, 삼각형 (가운데, k+1, k) — 위에서 보아 시계 방향(Unity 앞면)
                int t0 = a.Pos.Count;
                a.Add(new Vector3(cx, top, cz), Vector3.up, new Vector2(U0, V0), 0f, cover);
                for (int k = 0; k < 6; k++)
                    a.Add(new Vector3(cx + r * rim[k].x, top, cz + r * rim[k].y), Vector3.up, new Vector2(U0 + UX * rim[k].x, V0 - VZ * rim[k].y), 1f, cover);
                var topIdx = a.Indices(topGroup);
                for (int k = 0; k < 6; k++) { topIdx.Add(t0); topIdx.Add(t0 + 1 + (k + 1) % 6); topIdx.Add(t0 + 1 + k); }

                // 옆면: 띠마다 면 6. 쿼드 (위k, 위k+1, 밑k+1, 밑k) — 밖에서 보아 시계 방향
                int s0 = a.Pos.Count;
                var sideIdx = a.Indices(sideGroup);
                foreach (var band in bands)
                {
                    float y0 = (float)band.Bottom, y1 = (float)band.Top;
                    var c = ParseTint(band.Tint);
                    for (int k = 0; k < 6; k++)
                    {
                        int k1 = (k + 1) % 6, v = a.Pos.Count;
                        float x0 = cx + r * rim[k].x, z0 = cz + r * rim[k].y, x1 = cx + r * rim[k1].x, z1 = cz + r * rim[k1].y;
                        a.Add(new Vector3(x0, y1, z0), sideNormal[k], Vector2.zero, 0f, c);
                        a.Add(new Vector3(x1, y1, z1), sideNormal[k], Vector2.zero, 0f, c);
                        a.Add(new Vector3(x1, y0, z1), sideNormal[k], Vector2.zero, 0f, c);
                        a.Add(new Vector3(x0, y0, z0), sideNormal[k], Vector2.zero, 0f, c);
                        sideIdx.Add(v); sideIdx.Add(v + 1); sideIdx.Add(v + 2);
                        sideIdx.Add(v); sideIdx.Add(v + 2); sideIdx.Add(v + 3);
                    }
                }
                spans[i] = new CellSpan(part, t0, s0, bands.Length, topGroup);
            }

            var parts = new Part[Parts];
            for (int p = 0; p < Parts; p++) parts[p] = acc[p].ToPart(p == 0 ? "Hex Main" : "Hex Distortion");
            return parts;
        }

        /// <summary>"#rrggbbaa" → 바이트 그대로(BoardLoader가 형식을 이미 봤다).</summary>
        public static Color32 ParseTint(string tint)
        {
            byte B(int at) => byte.Parse(tint.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color32(B(1), B(3), B(5), B(7));
        }

        sealed class Acc
        {
            public readonly List<Vector3> Pos = new List<Vector3>();
            readonly List<Vector3> nrm = new List<Vector3>();
            readonly List<Vector2> uv0 = new List<Vector2>();
            readonly List<Vector2> uv1 = new List<Vector2>();
            readonly List<Color32> col = new List<Color32>();
            readonly List<int>[] idx = new List<int>[System.Enum.GetValues(typeof(HexGroup)).Length];

            public void Add(Vector3 p, Vector3 n, Vector2 uv, float rimness, Color32 c)
            {
                Pos.Add(p); nrm.Add(n); uv0.Add(uv); uv1.Add(new Vector2(rimness, 0f)); col.Add(c);
            }

            public List<int> Indices(HexGroup g) => idx[(int)g] ?? (idx[(int)g] = new List<int>());

            public Part ToPart(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = Pos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(Pos);
                mesh.SetNormals(nrm);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetColors(col);
                var groups = new List<HexGroup>();
                for (int g = 0; g < idx.Length; g++) if (idx[g] != null) groups.Add((HexGroup)g);
                mesh.subMeshCount = groups.Count;
                for (int s = 0; s < groups.Count; s++) mesh.SetTriangles(idx[(int)groups[s]], s, false);
                mesh.RecalculateBounds();
                return new Part { Mesh = mesh, Groups = groups.ToArray() };
            }
        }
    }
}
