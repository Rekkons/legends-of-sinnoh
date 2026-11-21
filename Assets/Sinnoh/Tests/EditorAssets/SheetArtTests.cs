using System.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Editor;
using Sinnoh.Fx;
using Sinnoh.Rules.Session;
using UnityEngine;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// 도트 이펙트 시트(결정 69, pixelfx — Gen <c>Stage/Fx</c>): 화면이 읽는 키가 표와 그림에 다 있고, 그림 크기가 표와 맞고, 플레이어 번들에 실리고, 자리의 시간 안에 다 돈다.
    /// Gen 본체가 없으면 건너뛴다(<c>node Tools/assets/build.mjs --only pixelfx</c>).
    /// </summary>
    public class SheetArtTests
    {
        [Test]
        public void M4_화면이읽는이펙트시트가표와그림에다있고번들에실린다()
        {
            if (GenAssets.Load<TextAsset>(FxSheet.TableKey) == null) Assert.Ignore("M4 Gen/Stage/Fx 없음 — node Tools/assets/build.mjs --only pixelfx");
            var planned = BoardBuild.Plan().SelectMany(b => b.addressableNames).ToList();
            Assert.That(GenAssets.BundleFor(FxSheet.TableKey), Is.EqualTo("stage"));
            Assert.That(planned, Does.Contain(FxSheet.TableKey));
            foreach (string key in SheetBeats.All())
            {
                var sheet = SheetArt.Of(key);
                Assert.That(sheet, Is.Not.Null, key + " — 표에 없다");
                var tex = SheetArt.Texture(sheet);
                Assert.That(tex, Is.Not.Null, key + " — 그림이 없다");
                Assert.That((tex.width, tex.height), Is.EqualTo((sheet.Fw * sheet.Frames, sheet.Fh)), key);
                Assert.That(tex.filterMode, Is.EqualTo(FilterMode.Point), key);
                Assert.That(sheet.Frames > 0 && sheet.Ms > 0 && !sheet.Loop, Is.True, key);
                Assert.That(GenAssets.BundleFor(SheetArt.TextureKey(sheet)), Is.EqualTo("stage"), key);
                Assert.That(planned, Does.Contain(SheetArt.TextureKey(sheet)), key);
                // DS 화면에 찍는 조각: 그 시각의 프레임 칸, 고정점이 준 자리에
                var ds = SheetArt.Ds(new FxStamp { Key = key, Ms = sheet.Ms, X = 100, Y = 80 });
                Assert.That((ds.Sx, ds.Sw, ds.Sh, ds.X, ds.Y), Is.EqualTo((sheet.Frames > 1 ? sheet.Fw : 0, sheet.Fw, sheet.Fh, 100 - sheet.Ox, 80 - sheet.Oy)), key);
                Assert.That(SheetArt.Ds(new FxStamp { Key = key, Ms = sheet.TotalMs }), Is.Null, key + " — 다 돈 뒤");
                Assert.That(SheetArt.Ds(new FxStamp { Key = key, Ms = -1 }), Is.Null, key + " — 시작 전");
            }
            // 자리의 시간 안에 다 돈다: 맞는 불꽃 ≤ 체력이 깎이는 단계 · 먼지 ≤ 항목 끝 뒤 화면이 도는 시간(클릭 막기) · 빛·반짝임 ≤ 포획 결말(실패 쪽이 짧다)
            Assert.That(SheetArt.Of(SheetBeats.Hit(Sinnoh.Rules.PokemonType.Fire, true)).TotalMs, Is.LessThanOrEqualTo(PlayTiming.HpDrainMs.Value));
            Assert.That(SheetArt.Of(SheetBeats.Dust).TotalMs, Is.LessThanOrEqualTo(FxTune.ClickGuardMs.Value));
            Assert.That(SheetArt.Of(SheetBeats.Open).TotalMs, Is.LessThanOrEqualTo(PlayTiming.CaptureFailTailMs.Value));
            Assert.That(SheetArt.Of(SheetBeats.Heal).TotalMs, Is.LessThanOrEqualTo(2800 * (1000 - HealBeats.FlashSpanPermille) / 1000));
            Assert.That(SheetArt.Of("none"), Is.Null);
        }
    }
}
