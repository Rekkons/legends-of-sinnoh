using System;
using System.Collections.Generic;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sinnoh.Client
{
    /// <summary>
    /// 굴림판 그리기(결정 49·50, spikes/dice-roll §5, 04 §9-1): 3D 주사위를 격자의 몇 배로 큰 RT에 그린 뒤 격자 크기로 줄이며 색을 제한하고 외곽선을 두른다(도트풍).
    /// 결과 <see cref="Output"/>(격자 크기 RT, Point)을 PC 층 요소의 배경으로 끼운다(<c>Background.FromRenderTexture</c>) — 요소 크기 = 격자 × <see cref="Dot"/>.
    /// <list type="bullet">
    /// <item>등급: 1080 토큰 = 격자 309×93 · 2px 도트 · 주사위 24칸(48px) · 4배로 그림 / 720 토큰 = 412×124 · 1px · 32칸(32px) · 3배. 판의 세계 크기는 같다(<see cref="DiceGeo"/>).</item>
    /// <item>카메라·씬 물체·레이어를 쓰지 않는다: 메시를 <c>CommandBuffer</c>로 바로 그린다(직교, 내려다보는 각 68° — 행렬을 셰이더에 직접 넣는다). 보드 카메라에 걸리지 않고 편집 모드 배치에서도 같다.</item>
    /// <item>자세는 <see cref="DiceRollScene.Pose"/>가 준다 — 여기에는 시간이 없다(같은 자세면 같은 그림).</item>
    /// </list>
    /// 셰이더는 <c>Art/Authored/Shaders/Dice*.shader</c>. 플레이어 빌드에 넣는 것(늘 포함 목록이나 재질 참조)은 굴림판을 쓰는 화면이 맡는다.
    /// </summary>
    public sealed class DiceBoard : IDisposable
    {
        const float Round = 0.09f;
        const int Cell = 24, MaxDiePal = 32, MaxFloorPal = 8;
        static readonly Color32 Ink = new Color32(0x28, 0x30, 0x38, 0xFF), Background = new Color32(0x2C, 0x38, 0x48, 0xFF), Floor = new Color32(0x22, 0x2C, 0x3A, 0xFF),
            Rim = new Color32(0x44, 0x54, 0x6C, 0xFF), CritRed = new Color32(0xD8, 0x28, 0x18, 0xFF), DimBody = new Color32(0xB0, 0xB8, 0xC0, 0xFF), DimEdge = new Color32(0x78, 0x80, 0x88, 0xFF);
        static readonly float[] Shades = { 1f, 0.56f, 0.33f };
        static readonly Vector3 LightDir = new Vector3(-0.35f, 0.85f, -0.4f);

        // 굵은 도트 글리프(r3 글리프 ×2): 일반 타격 1 · 급소 2 · 상태이상 심볼
        static readonly string[] GlyphRegular = { "..##..", ".###..", "..##..", "..##..", "..##..", "..##..", ".####." };
        static readonly string[] GlyphCritical = { ".####.", "##..##", "....##", "...##.", "..##..", ".##...", "######" };
        static readonly string[] GlyphStatus = { "..####..", ".###..#.", "####...#", "####...#", "####...#", "####...#", ".###..#.", "..####.." };

        static readonly int IdVP = Shader.PropertyToID("_DiceVP"), IdLight = Shader.PropertyToID("_LightDir"), IdDim = Shader.PropertyToID("_Dim"), IdFlash = Shader.PropertyToID("_Flash"),
            IdColor = Shader.PropertyToID("_Color"), IdBlob = Shader.PropertyToID("_Blob"), IdMainTex = Shader.PropertyToID("_MainTex"), IdDieTex = Shader.PropertyToID("_DieTex"),
            IdHiTex = Shader.PropertyToID("_HiTex"), IdLowTex = Shader.PropertyToID("_LowTex"), IdSize = Shader.PropertyToID("_Size"), IdGlitch = Shader.PropertyToID("_Glitch"),
            IdSS = Shader.PropertyToID("_SS"), IdDiePal = Shader.PropertyToID("_DiePal"), IdFloorPal = Shader.PropertyToID("_FloorPal"), IdDiePalCount = Shader.PropertyToID("_DiePalCount"),
            IdFloorPalCount = Shader.PropertyToID("_FloorPalCount"), IdInk = Shader.PropertyToID("_Ink"), IdCenter = Shader.PropertyToID("_Center");

        /// <summary>격자 크기(칸)와 한 칸의 패널 px. 요소 크기 = (GridW × Dot, GridH × Dot).</summary>
        public readonly int GridW, GridH, Dot;
        /// <summary>격자의 몇 배로 그리나.</summary>
        public readonly int Supersample;
        /// <summary>주사위 한 변의 격자 칸 수.</summary>
        public readonly float CellsPerUnit;
        /// <summary>도트풍으로 줄인 그림(격자 크기, Point). 배경으로 끼운다.</summary>
        public readonly RenderTexture Output;
        /// <summary>토큰 벌(만들 때 받은 값 — 벌이 바뀌면 새로 만든다).</summary>
        public readonly int TokenHeight;
        /// <summary>면 그림 한 칸의 텍셀(<see cref="Faces"/> — 가로 셋: 일반 · 심볼 · 급소, 세로 둘: 몸 색 · 회색).</summary>
        public const int FaceCell = Cell;
        /// <summary>면 그림(주사위에 붙이는 그림 그대로 — 굴림판 머리의 결과 줄이 같은 그림을 쓴다). <see cref="FaceRect"/>로 한 칸을 고른다.</summary>
        public Texture2D Faces => atlas;

        /// <summary>면 그림에서 그 면의 칸(왼쪽 위 원점 텍셀 — UI Toolkit <c>Image.sourceRect</c>). grey = 면 바꾸기로 바뀐 주사위의 회색 칸.</summary>
        public static Rect FaceRect(AttackDieFace face, bool grey)
        {
            int col = face == AttackDieFace.CriticalHit ? 2 : face == AttackDieFace.StatusSymbol ? 1 : 0;
            return new Rect(col * Cell, grey ? 0 : Cell, Cell, Cell);
        }

        /// <summary>
        /// 기술 타입의 주사위 몸 색(spikes/dice-roll §5 「색 = 기술 타입 색」 [가정 — 색 값은 포켓몬 타입 색 관례에서 골랐다, 면 그림의 잉크가 읽히는 밝기로]).
        /// 타입 없음(플랜 B)은 회백색.
        /// </summary>
        public static Color32 TypeBody(PokemonType type)
        {
            switch (type)
            {
                case PokemonType.Normal: return new Color32(0xC8, 0xC8, 0xA0, 0xFF);
                case PokemonType.Fire: return new Color32(0xF0, 0x80, 0x30, 0xFF);
                case PokemonType.Water: return new Color32(0x68, 0x90, 0xF0, 0xFF);
                case PokemonType.Electric: return new Color32(0xF8, 0xD0, 0x30, 0xFF);
                case PokemonType.Grass: return new Color32(0x78, 0xC8, 0x50, 0xFF);
                case PokemonType.Ice: return new Color32(0x98, 0xD8, 0xD8, 0xFF);
                case PokemonType.Fighting: return new Color32(0xD8, 0x58, 0x48, 0xFF);
                case PokemonType.Poison: return new Color32(0xB8, 0x68, 0xC0, 0xFF);
                case PokemonType.Ground: return new Color32(0xE0, 0xC0, 0x68, 0xFF);
                case PokemonType.Flying: return new Color32(0xA8, 0x90, 0xF0, 0xFF);
                case PokemonType.Psychic: return new Color32(0xF8, 0x58, 0x88, 0xFF);
                case PokemonType.Bug: return new Color32(0xA8, 0xB8, 0x20, 0xFF);
                case PokemonType.Rock: return new Color32(0xC8, 0xB0, 0x58, 0xFF);
                case PokemonType.Ghost: return new Color32(0x98, 0x80, 0xC8, 0xFF);
                case PokemonType.Dragon: return new Color32(0x90, 0x68, 0xF8, 0xFF);
                case PokemonType.Dark: return new Color32(0xA0, 0x88, 0x78, 0xFF);
                case PokemonType.Steel: return new Color32(0xB8, 0xB8, 0xD0, 0xFF);
                case PokemonType.Fairy: return new Color32(0xF0, 0xA8, 0xC0, 0xFF);
                default: return new Color32(0xE0, 0xE4, 0xE8, 0xFF);
            }
        }

        readonly float marginLeft, marginBottom, sinP, cosP;
        readonly RenderTexture hi, hiDie, low;
        readonly Texture2D atlas;
        readonly Mesh die, quad;
        readonly Material lit, flat, glitchMat, dot;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly CommandBuffer cmd = new CommandBuffer { name = "DiceBoard" };
        readonly Vector4[] diePal = new Vector4[MaxDiePal], floorPal = new Vector4[MaxFloorPal];
        readonly Matrix4x4 viewProj;

        /// <param name="tokenHeight">토큰 벌(720·1080 — <c>UiGrade.TokenHeight</c>).</param>
        /// <param name="body">주사위 몸 색(기술 타입 색 [가정] — <see cref="SetBody"/>로 바꾼다).</param>
        public DiceBoard(int tokenHeight, Color32 body)
        {
            TokenHeight = tokenHeight;
            bool big = tokenHeight >= 1080;
            GridW = big ? 309 : 412;
            GridH = big ? 93 : 124;
            Dot = big ? 2 : 1;
            Supersample = big ? 4 : 3;
            CellsPerUnit = big ? 24f : 32f;
            marginLeft = marginBottom = big ? 4.5f : 6f;
            sinP = Mathf.Sin(DiceGeo.PitchDeg * Mathf.Deg2Rad);
            cosP = Mathf.Cos(DiceGeo.PitchDeg * Mathf.Deg2Rad);

            lit = Make("Sinnoh/DiceLit");
            flat = Make("Sinnoh/DiceFlat");
            glitchMat = Make("Sinnoh/DiceGlitch");
            dot = Make("Sinnoh/DiceDot");
            hi = Target("DiceHi", GridW * Supersample, GridH * Supersample, 24);
            hiDie = Target("DiceHiDie", GridW * Supersample, GridH * Supersample, 24);
            low = Target("DiceLow", GridW, GridH, 0);
            Output = Target("DiceOut", GridW, GridH, 0);
            atlas = new Texture2D(Cell * 3, Cell * 2, TextureFormat.RGBA32, false, false) { name = "DiceFaces", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            die = RoundedCube();
            quad = Quad();

            // 직교 카메라: 판 가운데를 68°로 내려다본다. 격자 한 칸 = 1 / CellsPerUnit
            var rot = Quaternion.Euler(DiceGeo.PitchDeg, 0, 0);
            var focus = new Vector3((GridW / 2f - marginLeft) / CellsPerUnit, 0, (GridH / 2f - marginBottom) / (CellsPerUnit * sinP));
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(focus - rot * Vector3.forward * 50f, rot, Vector3.one).inverse;
            float hw = GridW / (2f * CellsPerUnit), hh = GridH / (2f * CellsPerUnit);
            viewProj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-hw, hw, -hh, hh, 0.5f, 120f), true) * view;

            floorPal[0] = Rgb(Background);
            floorPal[1] = Rgb(Floor);
            floorPal[2] = Rgb(Rim);
            floorPal[3] = Rgb(Shade(Floor, 0.45f));
            floorPal[4] = Rgb(Shade(Floor, 0.7f));
            SetBody(body);
        }

        static Material Make(string shader)
        {
            var s = Shader.Find(shader);
            if (s == null || !s.isSupported) throw new InvalidOperationException("셰이더 없음(또는 컴파일 오류): " + shader);
            return new Material(s) { hideFlags = HideFlags.DontSave };
        }

        static RenderTexture Target(string name, int w, int h, int depth)
        {
            var rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1, useMipMap = false, hideFlags = HideFlags.DontSave,
            };
            rt.Create();
            return rt;
        }

        static Vector4 Rgb(Color32 c) => new Vector4(c.r, c.g, c.b, 255);
        static Vector4 Linear(Color32 c, float a = 1f)
        {
            var l = ((Color)c).linear;
            return new Vector4(l.r, l.g, l.b, a);
        }

        /// <summary>sRGB 색을 Linear에서 s배 한 색(셰이더의 단계 음영과 같은 셈).</summary>
        static Color32 Shade(Color32 c, float s)
        {
            byte F(byte v) => (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(v / 255f) * s) * 255f);
            return new Color32(F(c.r), F(c.g), F(c.b), 255);
        }

        /// <summary>주사위 몸 색을 바꾼다: 면 그림(아래 줄 = 몸 색, 윗줄 = 회색)과 색 제한 팔레트(면 그림의 색 × 음영 3단 + 흰색)를 다시 만든다.</summary>
        public void SetBody(Color32 body)
        {
            body.a = 255;
            var edge = new Color32((byte)(body.r * 0.77f), (byte)(body.g * 0.77f), (byte)(body.b * 0.77f), 255);
            var px = new Color32[Cell * 3 * Cell * 2];
            string[][] glyphs = { GlyphRegular, GlyphStatus, GlyphCritical };   // 칸 0 일반 · 1 심볼 · 2 급소(메시 UV와 같은 순서)
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                    Face(px, col * Cell, row * Cell, glyphs[col], row == 0 ? body : DimBody, row == 0 ? edge : DimEdge, col == 2 && row == 0);
            atlas.SetPixels32(px);
            atlas.Apply(false);

            var uniq = new List<Color32> { body, edge, Ink, CritRed, DimBody, DimEdge };
            int n = 0;
            foreach (var c in uniq)
                foreach (float s in Shades)
                    diePal[n++] = Rgb(Shade(c, s));
            diePal[n++] = new Vector4(255, 255, 255, 255);
            dot.SetVectorArray(IdDiePal, diePal);
            dot.SetFloat(IdDiePalCount, n);
            dot.SetVectorArray(IdFloorPal, floorPal);
            dot.SetFloat(IdFloorPalCount, 5);
        }

        // 면 그림 24×24: 몸 단색 + 어두운 테두리 1칸 + 글리프(잉크색). 급소는 빨간 글리프에 잉크 외곽선
        static void Face(Color32[] px, int ox, int oy, string[] glyph, Color32 body, Color32 edge, bool red)
        {
            int gw = glyph[0].Length * 2, gh = glyph.Length * 2, gx = (Cell - gw) / 2, gy = (Cell - gh) / 2;
            bool On(int x, int y)
            {
                if (x < gx || y < gy) return false;
                int cx = (x - gx) / 2, cy = (y - gy) / 2;
                return cy < glyph.Length && cx < glyph[0].Length && glyph[cy][cx] == '#';
            }
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    Color32 c = x == 0 || y == 0 || x == Cell - 1 || y == Cell - 1 ? edge : body;
                    if (On(x, y)) c = red ? CritRed : Ink;
                    else if (red && (On(x - 1, y) || On(x + 1, y) || On(x, y - 1) || On(x, y + 1))) c = Ink;
                    px[(oy + Cell - 1 - y) * Cell * 3 + ox + x] = c;   // 그림은 위에서 아래로, 텍스처는 아래에서 위로
                }
        }

        /// <summary>모서리를 둥글린 정육면체. 면 번호·그림 위쪽은 <see cref="DiceCube"/>와 같다. UV = 아틀라스 아래 줄의 칸(일반 0 · 심볼 1 · 급소 2).</summary>
        static Mesh RoundedCube()
        {
            const float h = 0.5f, r = Round;
            float[] cs = { -h, -h + r * 0.35f, -h + r * 0.7f, -h + r, h - r, h - r * 0.7f, h - r * 0.35f, h };
            Vector3[] right = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
            int m = cs.Length;
            float inset = 0.5f / Cell;
            var vs = new List<Vector3>();
            var ns = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int f = 0; f < 6; f++)
            {
                var normal = V(DiceCube.Normal[f]);
                var up = V(DiceCube.GlyphUp[f]);
                int cell = DiceCube.FaceAt[f] == Rules.AttackDieFace.CriticalHit ? 2 : DiceCube.FaceAt[f] == Rules.AttackDieFace.StatusSymbol ? 1 : 0;
                int b0 = vs.Count;
                for (int iy = 0; iy < m; iy++)
                    for (int ix = 0; ix < m; ix++)
                    {
                        var p = normal * h + right[f] * cs[ix] + up * cs[iy];
                        var inner = new Vector3(Mathf.Clamp(p.x, -(h - r), h - r), Mathf.Clamp(p.y, -(h - r), h - r), Mathf.Clamp(p.z, -(h - r), h - r));
                        var nn = (p - inner).normalized;
                        vs.Add(inner + nn * r);
                        ns.Add(nn);
                        uv.Add(new Vector2((cell + Mathf.Lerp(inset, 1 - inset, cs[ix] + h)) / 3f, Mathf.Lerp(inset, 1 - inset, cs[iy] + h) / 2f));
                    }
                for (int iy = 0; iy < m - 1; iy++)
                    for (int ix = 0; ix < m - 1; ix++)
                    {
                        int a = b0 + iy * m + ix;
                        tri.Add(a); tri.Add(a + m); tri.Add(a + m + 1);
                        tri.Add(a); tri.Add(a + m + 1); tri.Add(a + 1);
                    }
            }
            var mesh = new Mesh { name = "Die", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vs);
            mesh.SetNormals(ns);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Quad()
        {
            var mesh = new Mesh { name = "DiceQuad", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return mesh;
        }

        static Vector3 V(V3 v) => new Vector3(v.X, v.Y, v.Z);
        static Quaternion Q(Q4 q) => new Quaternion(q.X, q.Y, q.Z, q.W);

        /// <summary>세계 좌표 → 격자 칸(왼쪽 위 원점).</summary>
        public Vector2 ToGrid(V3 w) => new Vector2(marginLeft + w.X * CellsPerUnit, GridH - marginBottom - (w.Z * sinP + w.Y * cosP) * CellsPerUnit);

        /// <summary>세계 좌표 → 요소 안 패널 px(왼쪽 위 원점). 주사위 고리·꼬리표를 놓을 자리.</summary>
        public Vector2 ToPanel(V3 w) => ToGrid(w) * Dot;

        void Flat(Vector3 center, Vector2 size, float yaw, Vector4 color, bool blob)
        {
            block.Clear();
            block.SetVector(IdColor, color);
            block.SetFloat(IdBlob, blob ? 1f : 0f);
            cmd.DrawMesh(quad, Matrix4x4.TRS(center, Quaternion.Euler(0, yaw, 0), new Vector3(size.x, 1, size.y)), flat, 0, 0, block);
        }

        void Die(in DiePose p)
        {
            block.Clear();
            block.SetFloat(IdDim, p.Dim);
            block.SetFloat(IdFlash, p.Flash);
            cmd.DrawMesh(die, Matrix4x4.TRS(V(p.Pos), Q(p.Rot), Vector3.one + V(p.Stretch)), lit, 0, 0, block);   // Stretch = 재굴림 글리치의 일그러짐(평소 0)
        }

        /// <summary>자세 <paramref name="poses"/>[0..count)를 그려 <see cref="Output"/>을 채운다.</summary>
        public void Render(DiePose[] poses, int count, DiceGlitch glitch)
        {
            float W = DiceGeo.Width, D = DiceGeo.Depth, cellWorld = 1f / CellsPerUnit;
            lit.SetTexture(IdMainTex, atlas);
            lit.SetVector(IdLight, LightDir);
            cmd.Clear();
            cmd.SetGlobalMatrix(IdVP, viewProj);
            cmd.SetRenderTarget(hi);
            cmd.ClearRenderTarget(true, true, Color.clear);
            Flat(new Vector3(W / 2, 0, D / 2), new Vector2(W + 80, D + 80), 0, Linear(Background), false);
            Flat(new Vector3(W / 2, 0, D / 2), new Vector2(W + 2 * cellWorld, D + 2 * cellWorld / sinP), 0, Linear(Rim), false);
            Flat(new Vector3(W / 2, 0, D / 2), new Vector2(W, D), 0, Linear(Floor), false);
            for (int i = 0; i < count; i++)
            {
                if (!poses[i].Shown) continue;
                // 그림자: 딱딱한 블롭이 높이만큼 밀리고 옅어진다
                var p = V(poses[i].Pos);
                var q = Q(poses[i].Rot);
                float height = Mathf.Max(0, p.y - 0.5f);
                var ax = q * Vector3.right;
                if (Mathf.Abs(ax.y) > 0.8f) ax = q * Vector3.forward;
                float size = 1.08f * (1f + 0.06f * Mathf.Min(height, 3f));
                Flat(new Vector3(p.x + 0.38f * height + 0.07f, 0, p.z + 0.2f * height - 0.05f), new Vector2(size, size), Mathf.Atan2(ax.x, ax.z) * Mathf.Rad2Deg,
                    new Vector4(0, 0, 0, 0.55f / (1f + 0.6f * height)), true);
            }
            for (int i = 0; i < count; i++)
                if (poses[i].Shown && !(glitch.On && glitch.Die == i)) Die(poses[i]);

            var center = glitch.On ? ToGrid(glitch.Center) : Vector2.zero;
            if (glitch.On && glitch.Die < count && poses[glitch.Die].Shown)
            {
                // 고른 주사위만 따로 그려 글리치를 입혀 얹는다
                cmd.SetRenderTarget(hiDie);
                cmd.ClearRenderTarget(true, true, Color.clear);
                Die(poses[glitch.Die]);
                float perW = CellsPerUnit / GridW;
                int bandCells = Mathf.Max(1, Mathf.RoundToInt(0.14f * CellsPerUnit)), lineCells = Mathf.Max(1, Mathf.RoundToInt(CellsPerUnit / 16f));
                glitchMat.SetTexture(IdDieTex, hiDie);
                glitchMat.SetVector(IdSize, new Vector4(hi.width, hi.height, 0, 0));
                glitchMat.SetVector(IdCenter, new Vector4(center.x / GridW, 1f - center.y / GridH, 0, 0));
                glitchMat.SetFloat("_Amt", glitch.Amt * perW);
                glitchMat.SetFloat("_Split", glitch.Split * perW);
                glitchMat.SetFloat("_Ghost", glitch.Ghost * perW);
                glitchMat.SetFloat("_Tint", glitch.Tint);
                glitchMat.SetFloat("_Clock", glitch.Clock);
                glitchMat.SetFloat("_Angle", glitch.Angle);
                glitchMat.SetFloat("_Seed", glitch.Seed);
                glitchMat.SetFloat("_Bands", (float)GridH / bandCells);
                glitchMat.SetFloat("_Lines", (float)GridH / lineCells);
                glitchMat.SetFloat("_Radius", glitch.ClockR * CellsPerUnit / GridH);
                glitchMat.SetFloat("_Aspect", (float)GridW / GridH);
                glitchMat.SetFloat("_Px", Mathf.Max(1f, CellsPerUnit / 16f) / GridH);
                cmd.SetRenderTarget(hi);
                cmd.DrawProcedural(Matrix4x4.identity, glitchMat, 0, MeshTopology.Triangles, 3);
            }

            // 격자로 줄이며 색 제한 → 외곽선
            dot.SetTexture(IdHiTex, hi);
            dot.SetTexture(IdLowTex, low);
            dot.SetVector(IdSize, new Vector4(GridW, GridH, 0, 0));
            dot.SetFloat(IdSS, Supersample);
            dot.SetVector(IdGlitch, new Vector4(center.x, center.y, 1.5f * CellsPerUnit, glitch.On ? 1 : 0));
            dot.SetVector(IdInk, Linear(Ink));
            cmd.SetRenderTarget(low);
            cmd.DrawProcedural(Matrix4x4.identity, dot, 0, MeshTopology.Triangles, 3);
            cmd.SetRenderTarget(Output);
            cmd.DrawProcedural(Matrix4x4.identity, dot, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        public void Dispose()
        {
            cmd.Release();
            foreach (var rt in new[] { hi, hiDie, low, Output })
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            foreach (var o in new UnityEngine.Object[] { atlas, die, quad, lit, flat, glitchMat, dot }) UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
