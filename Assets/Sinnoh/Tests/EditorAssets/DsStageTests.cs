using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// DS 층(04 §4-2·§4-7, plan §4 「배틀 화면 뼈대」): 256×192 RT에 무대·도트·체력 상자·메시지 창이 정한 자리에 그려지는가, 원작 자산 글꼴이 배틀 화면의 글자를 모두 갖고 있는가,
    /// 지형 짝 표가 모든 지형·기후에 무대를 주는가. Gen 본체(무대·글꼴·정지 도트)가 있어야 돈다.
    /// </summary>
    public class DsStageTests
    {
        const string Gen = "Assets/Sinnoh/Gen/";

        static void NeedGen()
        {
            foreach (var key in new[] { DsStage.StageTableKey, DsStage.EnemyBoxKey, DsFont.TableKey(DsStage.MessageFont), MonArt.TableKey })
                if (!File.Exists(Gen + key)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
        }

        static void NeedGraphics()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
        }

        static Color32[] Read(RenderTexture rt)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        // DS px(왼쪽 위가 (0, 0)) → 읽은 배열(아래 줄부터)
        static Color32 At(Color32[] px, int x, int y) => px[(DsStage.Height - 1 - y) * DsStage.Width + x];

        static bool Near(Color32 want, Color32 got) =>
            Mathf.Abs(want.r - got.r) <= 2 && Mathf.Abs(want.g - got.g) <= 2 && Mathf.Abs(want.b - got.b) <= 2 && got.a == 255;

        [Test]
        public void M4_HP막대는남은만큼이고남았으면한칸은보인다()
        {
            Assert.AreEqual(48, DsStage.BarPixels(9, 9, 48));
            Assert.AreEqual(0, DsStage.BarPixels(0, 9, 48));
            Assert.AreEqual(24, DsStage.BarPixels(4, 8, 48));
            Assert.AreEqual(26, DsStage.BarPixels(5, 9, 48), "내림");
            Assert.AreEqual(1, DsStage.BarPixels(1, 100, 48), "체력이 남았으면 1칸은 남긴다");
            Assert.AreEqual(48, DsStage.BarPixels(12, 9, 48), "넘치는 값은 꽉 찬 막대");
            Assert.AreEqual(0, DsStage.BarPixels(3, 0, 48));
        }

        [Test]
        public void M4_DS층은256x192에무대와체력상자와메시지창을정한자리에그린다()
        {
            NeedGen();
            NeedGraphics();
            var ds = DsStage.Create();
            Assert.IsNotNull(ds, "DS 층(셰이더 " + DsStage.ShaderName + " · 글꼴 · 체력 상자 · 정지 도트)");
            try
            {
                Assert.AreEqual(256, ds.Output.width);
                Assert.AreEqual(192, ds.Output.height);
                Assert.AreEqual(FilterMode.Point, ds.Output.filterMode);
                Assert.IsTrue(DsStage.Terrain(Biome.Plains, Climate.Temperate, out string bg, out string enemy, out string player));
                var scene = new DsScene
                {
                    Bg = bg, EnemyPlatform = enemy, PlayerPlatform = player, Message = "가나다 무엇을 할까?",
                    Enemy = new DsMon { Key = "436:0@male_grunt", Name = "동미러", Hp = 9, MaxHp = 9, Speed = 2, Fatigue = 0 },
                    Player = new DsMon { Key = "390:0", Name = "불꽃숭이", Hp = 2, MaxHp = 4, Speed = 6, Fatigue = 5, Status = StatusEffect.Burned },
                };
                ds.Draw(scene);
                var full = Read(ds.Output);
                var colors = new HashSet<Color32>(full);
                Assert.Greater(colors.Count, 32, "색 수(배경 · 발판 · 도트 · 상자 · 글)");
                foreach (var p in full) Assert.AreEqual(255, p.a, "DS 화면은 불투명하다");

                // HP 막대: 상대 꽉 참(초록), 내 쪽 반(노랑 — 반 이하), 막대 끝 뒤는 막대 색이 아니다
                var green = new Color32(0x18, 0xC5, 0x20, 255);
                var yellow = new Color32(0xEE, 0xAC, 0x00, 255);
                var red = new Color32(0xFF, 0x41, 0x10, 255);
                int ex = DsStage.EnemyBoxAt.x + 56, ey = DsStage.EnemyBoxAt.y + 35, px = DsStage.PlayerBoxAt.x + 72, py = DsStage.PlayerBoxAt.y + 35;
                Assert.IsTrue(Near(green, At(full, ex, ey)), "상대 막대 첫 칸 " + At(full, ex, ey));
                Assert.IsTrue(Near(green, At(full, ex + 47, ey + 1)), "상대 막대 끝 칸 " + At(full, ex + 47, ey + 1));
                Assert.IsTrue(Near(yellow, At(full, px, py)), "내 막대 첫 칸 " + At(full, px, py));
                Assert.IsTrue(Near(yellow, At(full, px + 23, py)), "내 막대 24번째 칸(4 중 2 = 24칸)");
                Assert.IsFalse(Near(yellow, At(full, px + 24, py)), "내 막대는 24칸에서 끝난다");
                // 전투 피로 5: 내 쪽 아래 다섯 칸이 모두 빨강
                for (int i = 0; i < 5; i++)
                    Assert.IsTrue(Near(red, At(full, DsStage.PlayerBoxAt.x + 24 + i * 20 + 8, DsStage.PlayerBoxAt.y + 50)), "피로 칸 " + (i + 1));
                // 메시지 창: 안쪽은 흰 바탕, 글자가 찍혔다(글자색 #5A5A52)
                var white = new Color32(255, 255, 255, 255);
                Assert.IsTrue(Near(white, At(full, 128, 176)), "메시지 창 안쪽 바탕 " + At(full, 128, 176));
                int ink = 0;
                for (int y = DsStage.MessageTextY; y < DsStage.MessageTextY + 16; y++)
                    for (int x = DsStage.MessageTextX; x < 160; x++)
                        if (Near(new Color32(0x5A, 0x5A, 0x52, 255), At(full, x, y))) ink++;
                Assert.Greater(ink, 40, "메시지 글자 픽셀");
                // 도트: 고정점 바로 위에 불투명한 그림이 선다(배경만 있는 그림과 다르다)
                ds.Draw(new DsScene { Bg = bg, EnemyPlatform = enemy, PlayerPlatform = player });
                var empty = Read(ds.Output);
                int enemyDiff = 0, playerDiff = 0;
                for (int y = DsStage.EnemyFoot.y - 40; y < DsStage.EnemyFoot.y; y++)
                    for (int x = DsStage.EnemyFoot.x - 20; x < DsStage.EnemyFoot.x + 20; x++)
                        if (!At(full, x, y).Equals(At(empty, x, y))) enemyDiff++;
                for (int y = DsStage.PlayerFoot.y - 44; y < 144; y++)
                    for (int x = DsStage.PlayerFoot.x - 20; x < DsStage.PlayerFoot.x + 20; x++)
                        if (!At(full, x, y).Equals(At(empty, x, y))) playerDiff++;
                Assert.Greater(enemyDiff, 200, "상대 앞모습(갤럭시단 카드 키 → 같은 종의 도트)");
                Assert.Greater(playerDiff, 200, "내 뒷모습");
                Assert.IsTrue(Near(white, At(empty, 128, 176)), "글이 없어도 메시지 창은 그린다");

                // 기절: 도트는 사라지고 상자는 남는다
                scene.Enemy.Hp = 0;
                scene.Enemy.Standing = false;
                ds.Draw(scene);
                var fainted = Read(ds.Output);
                int still = 0;
                for (int y = DsStage.EnemyFoot.y - 40; y < DsStage.EnemyFoot.y; y++)
                    for (int x = DsStage.EnemyFoot.x - 20; x < DsStage.EnemyFoot.x + 20; x++)
                        if (!At(fainted, x, y).Equals(At(empty, x, y))) still++;
                Assert.AreEqual(0, still, "기절한 포켓몬의 도트는 그리지 않는다");
                Assert.IsFalse(Near(green, At(fainted, ex, ey)), "체력 0: 막대가 비었다");

                // 같은 장면을 다시 그리면 픽셀이 같다
                ds.Draw(scene);
                CollectionAssert.AreEqual(fainted, Read(ds.Output), "두 번 그려 같은 그림");
            }
            finally
            {
                ds.Dispose();
            }
        }

        [Test]
        public void M4_주인공뒷모습은내포켓몬보다앞이고체력상자와메시지창보다뒤다_결정61()
        {
            NeedGen();
            NeedGraphics();
            const string backKey = "Trainers/Back/official/pt_player_male.png";
            if (!File.Exists(Gen + backKey)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (" + backKey + ")");
            var ds = DsStage.Create();
            Assert.IsNotNull(ds);
            try
            {
                Assert.IsTrue(DsStage.Terrain(Biome.Plains, Climate.Temperate, out string bg, out string enemy, out string player));
                DsMon Mine() => new DsMon { Key = "390:0", Name = "불꽃숭이", Hp = 4, MaxHp = 4, Speed = 6 };
                // 뒷모습(서 있는 프레임 80×80)을 내 포켓몬 위에 겹쳐 놓는다
                DsSprite Back(int x, int y) => new DsSprite { Key = backKey, Sw = 80, Sh = 80, X = x, Y = y };
                DsScene Scene(DsMon mon, DsSprite back, string message = null) => new DsScene { Bg = bg, EnemyPlatform = enemy, PlayerPlatform = player, Player = mon, Back = back, Message = message };
                Color32[] Shot(DsScene s) { ds.Draw(s); return Read(ds.Output); }

                int bx = DsStage.PlayerFoot.x - 40, by = DsStage.PlayerFoot.y - 80;
                var empty = Shot(Scene(null, null));
                var hidden = Mine();
                hidden.BoxShown = false;
                var monOnly = Shot(Scene(hidden, null));
                var backOnly = Shot(Scene(null, Back(bx, by)));
                var both = Shot(Scene(hidden, Back(bx, by)));
                Assert.AreEqual(backKey, ds.BackDrawn);
                int overlap = 0;
                for (int y = by; y < DsStage.MessageRect.y; y++)
                    for (int x = bx; x < bx + 80; x++)
                    {
                        if (At(backOnly, x, y).Equals(At(empty, x, y))) continue;   // 뒷모습의 빈 칸
                        Assert.AreEqual(At(backOnly, x, y), At(both, x, y), "뒷모습 칸 (" + x + ", " + y + ")은 포켓몬에 가리지 않는다");
                        if (!At(monOnly, x, y).Equals(At(empty, x, y))) overlap++;
                    }
                Assert.Greater(overlap, 100, "겹친 칸이 있어야 앞뒤를 본 것이다");

                // 체력 상자·메시지 창 위에 뒷모습을 놓아도 상자와 창은 그대로다
                int ox = DsStage.PlayerBoxAt.x + 24, oy = DsStage.MessageRect.y - 60;
                var boxed = Shot(Scene(Mine(), null, "가나다"));
                var under = Shot(Scene(Mine(), Back(ox, oy), "가나다"));
                var bare = Shot(Scene(hidden, null, "가나다"));
                int boxPx = 0;
                for (int y = oy; y < DsStage.Height; y++)
                    for (int x = ox; x < ox + 80; x++)
                    {
                        bool box = !At(boxed, x, y).Equals(At(bare, x, y)), window = y >= DsStage.MessageRect.y + 8;   // 창 안쪽(창틀 그림의 투명한 가장자리 줄은 뺀다)
                        if (!box && !window) continue;
                        Assert.AreEqual(At(boxed, x, y), At(under, x, y), "체력 상자·메시지 창 칸 (" + x + ", " + y + ")은 뒷모습에 가리지 않는다");
                        if (box) boxPx++;
                    }
                Assert.Greater(boxPx, 100, "체력 상자와 겹친 칸");
                CollectionAssert.AreNotEqual(boxed, under, "상자 밖에서는 뒷모습이 보인다");
            }
            finally
            {
                ds.Dispose();
            }
        }

        [Test]
        public void M4_지형짝표는모든지형과기후에무대그림을준다()
        {
            NeedGen();
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
                foreach (Climate climate in System.Enum.GetValues(typeof(Climate)))
                {
                    Assert.IsTrue(DsStage.Terrain(biome, climate, out string bg, out string enemy, out string player), biome + "×" + climate);
                    foreach (string key in new[] { bg, enemy, player })
                        Assert.IsNotNull(GenAssets.Load<Texture2D>(key), biome + "×" + climate + " → " + key);
                }
            // 표에 있는 짝은 그 줄 그대로다
            var rows = JObject.Parse(File.ReadAllText(Gen + DsStage.StageTableKey))["terrain"];
            int checkedRows = 0;
            foreach (var r in rows)
            {
                if ((bool)r["distortion"] || r["biome"].Type == JTokenType.Null) continue;
                var biome = (Biome)System.Enum.Parse(typeof(Biome), (string)r["biome"]);
                var climate = (Climate)System.Enum.Parse(typeof(Climate), (string)r["climate"]);
                DsStage.Terrain(biome, climate, out string bg, out string enemy, out string player);
                Assert.AreEqual("Stage/" + (string)r["bg"], bg);
                Assert.AreEqual("Stage/" + (string)r["enemy"], enemy);
                Assert.AreEqual("Stage/" + (string)r["player"], player);
                checkedRows++;
            }
            Assert.AreEqual(12, checkedRows, "지형 4 × 기후 3");

            // 시간대(결정 70 ⑥): 야외 짝은 저녁·밤에 그림 셋이 모두 바뀌고 그 그림이 있다 · 낮은 표 그대로 · 시간대 그림이 없는 짝(테마 없는 칸의 동굴)은 그대로
            var timedSet = new HashSet<string>();
            foreach (var t in JObject.Parse(File.ReadAllText(Gen + DsStage.StageTableKey))["timed"]) timedSet.Add("Stage/" + (string)t);
            int changed = 0;
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
                foreach (Climate climate in System.Enum.GetValues(typeof(Climate)))
                {
                    DsStage.Terrain(biome, climate, out string dayBg, out string dayEnemy, out string dayPlayer);
                    DsStage.Terrain(biome, climate, Sinnoh.Fx.DayTime.Day, out string bg0, out string enemy0, out string player0);
                    Assert.AreEqual((dayBg, dayEnemy, dayPlayer), (bg0, enemy0, player0), "낮 = 표 그대로");
                    foreach (var time in new[] { Sinnoh.Fx.DayTime.Evening, Sinnoh.Fx.DayTime.Night })
                    {
                        Assert.IsTrue(DsStage.Terrain(biome, climate, time, out string bg, out string enemy, out string player));
                        Assert.AreEqual(Sinnoh.Fx.DayTimes.Art(dayBg, time, timedSet.Contains(dayBg)), bg, biome + "×" + climate + " " + time);
                        Assert.AreEqual(Sinnoh.Fx.DayTimes.Art(dayEnemy, time, timedSet.Contains(dayEnemy)), enemy);
                        Assert.AreEqual(Sinnoh.Fx.DayTimes.Art(dayPlayer, time, timedSet.Contains(dayPlayer)), player);
                        if (bg != dayBg && enemy != dayEnemy && player != dayPlayer) changed++;
                        foreach (string key in new[] { bg, enemy, player })
                            Assert.IsNotNull(GenAssets.Load<Texture2D>(key), biome + "×" + climate + " " + time + " → " + key);
                    }
                }
            Assert.GreaterOrEqual(changed, 12 * 2, "지형 4 × 기후 3의 저녁·밤은 그림 셋이 모두 바뀐다");
        }

        [Test]
        public void M4_원작자산글꼴은배틀화면의글자를모두갖고있다()
        {
            NeedGen();
            var message = DsFont.Load(DsStage.MessageFont);
            var hud = DsFont.Load(DsStage.HudFont);
            Assert.IsNotNull(message);
            Assert.IsNotNull(hud);
            Assert.AreEqual(16, message.Cell);
            Assert.AreEqual(12, message.Of('가').Advance, "한글 폭");
            Assert.AreEqual(12, message.Width("가"));
            Assert.AreEqual(message.Width("가나") + message.Of(' ').Advance + message.Width("다"), message.Width("가나 다"));
            var table = UiShotsStrings();
            var missing = new List<string>();
            foreach (string key in table.Keys)
            {
                // 메시지 창에 뜨는 글: 배틀 기록 줄 · 물음, 이름 칸에 뜨는 글: 포켓몬 이름(체력 상자), 기술 이름(기록 줄의 빈칸)
                bool inMessage = key.StartsWith("screen.battle.log.") || key.StartsWith("screen.battle.ds.") || key == "screen.battle.guide.token" || key == "screen.battle.move.plan_b";
                bool name = key.StartsWith("pokemon.") || (key.StartsWith("move.") && !key.EndsWith(".text"));
                if (!inMessage && !name) continue;
                foreach (char c in table[key])
                {
                    if (c == '{' || c == '}') continue;
                    if (c >= 'a' && c <= 'z' && inMessage) continue;   // 빈칸 이름({mon})
                    if (!message.Has(c)) missing.Add(key + " 「" + c + "」(메시지)");
                    if (key.StartsWith("pokemon.") && !hud.Has(c)) missing.Add(key + " 「" + c + "」(체력 상자)");
                }
            }
            CollectionAssert.IsEmpty(missing, "원작 자산 글꼴에 없는 글자 — 대체 글리프(')로 찍힌다");
        }

        static Sinnoh.Data.StringTable UiShotsStrings() => Sinnoh.Editor.UiShots.Strings();

        [Test]
        public void M4_체력상자의긴이름은말줄임표로줄인다()
        {
            NeedGen();
            var hud = DsFont.Load(DsStage.HudFont);
            Assert.IsTrue(hud.Has(DsStage.Ellipsis[0]), "말줄임표 글자");
            int width = DsStage.NameWidth(true, 3);
            Assert.AreEqual(DsStage.NameWidth(true, 3) - 8, DsStage.NameWidth(true, 12), "스피드가 두 자리면 이름 칸이 숫자 한 칸만큼 준다");
            Assert.AreEqual("동미러", DsStage.Fit(hud, "동미러", width), "들어가는 이름은 그대로");
            const string coral = "깝질무 (동쪽바다 폼)";
            Assert.Greater(hud.Width(coral), width, "넘치는 이름");
            string cut = DsStage.Fit(hud, coral, width);
            Assert.That(cut, Does.EndWith(DsStage.Ellipsis), "잘린 이름은 「…」로 끝난다(글자 중간에서 그냥 끊기지 않는다)");
            Assert.That(cut, Does.StartWith("깝질무 (동쪽"));
            Assert.LessOrEqual(hud.Width(cut), width);
            Assert.Greater(hud.Width(cut.Substring(0, cut.Length - 1) + coral[cut.Length - 1] + DsStage.Ellipsis), width, "들어가는 만큼은 다 적는다");
            Assert.AreEqual("깝질무…", DsStage.Fit(hud, coral, hud.Width("깝질무 (") + hud.Width(DsStage.Ellipsis)), "끝에 빈칸·여는 괄호를 남기지 않는다");
            // 문자열 표의 모든 포켓몬 이름: 양쪽 상자에서 폭 안이고, 줄였으면 「…」로 끝난다
            var table = UiShotsStrings();
            int shortened = 0;
            foreach (string key in table.Keys)
            {
                if (!key.StartsWith("pokemon.")) continue;
                foreach (bool enemy in new[] { true, false })
                {
                    int w = DsStage.NameWidth(enemy, 12);
                    string fit = DsStage.Fit(hud, table[key], w);
                    Assert.LessOrEqual(hud.Width(fit), w, key);
                    if (fit != table[key])
                    {
                        Assert.That(fit, Does.EndWith(DsStage.Ellipsis), key);
                        shortened++;
                    }
                }
            }
            Assert.Greater(shortened, 0, "줄여야 하는 이름이 있다(폼 이름)");
        }

        [Test]
        public void M4_글줄바꿈은빈칸에서끊고폭을넘지않는다()
        {
            NeedGen();
            var font = DsFont.Load(DsStage.MessageFont);
            int width = DsStage.MessageTextRight - DsStage.MessageTextX;
            var one = DsStage.Wrap(font, "불꽃숭이는 무엇을 할까?", width);
            Assert.AreEqual(1, one.Count);
            var two = DsStage.Wrap(font, "갤럭시단 조무래기는 동미러를 내보냈다!", width);
            Assert.AreEqual(2, two.Count, "긴 줄은 둘로");
            foreach (string line in two) Assert.LessOrEqual(font.Width(line), width, line);
            Assert.AreEqual("갤럭시단 조무래기는 동미러를 내보냈다!", string.Join(" ", two), "글자를 잃지 않는다");
            Assert.AreEqual(2, DsStage.Wrap(font, "가\n나", width).Count, "줄 바꿈 글자");
        }
    }
}
