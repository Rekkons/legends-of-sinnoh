using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 실행 감사 ④ 트레이너 능력 충돌 장면(2026-11-20 사용자 — ③ 충돌 쌍의 봇 판이 카드 능력끼리 같은 순간을 거의 만들지 못한 것을 메운다).
    /// 봇 판에 기대지 않고 상태를 짜서 쌍마다 조건(주사위 · 타입이 맞는 기술 · 해금 · 자원)을 맞추고, 「둘 다 씀 · A만 · B만」을 돌려 적용 순서(rulings G6) · 결과 · 한도 · 둘째 바퀴를 단정한다.
    /// <para>표 둘: <see cref="Sources"/> = 창마다 쓸 수 있는 출처(트레이너 카드 능력 102 · 운명 카드 · 지닌물건) — 카드 능력은 데이터(<c>trainer_cards</c>)에서 다 훑어 분류가 빠지면 시험이 실패한다.
    /// 거기서 「다른 좌석끼리 같은 창」 후보 쌍을 뽑고(<see cref="Candidates"/>), <see cref="Rows"/>가 대표 쌍의 장면이다(덮은 범위는 <see cref="Coverage"/>).</para>
    /// 게임 동작은 바꾸지 않는다: 기대와 다른 실제는 <see cref="Known"/>(찾은 것 목록 — 재현이 사라지면 시험이 알린다).
    /// </summary>
    public static partial class ExecAuditClashes
    {
        // ── 출처 분류(창 · 자리) ──

        /// <summary>창(G6 묶음 단위). Own = 자기 행동 안(맵 행동 · 정비 · 제작 · 배틀 행동 단추) — 남과 같은 순간에 겹치지 않아 쌍에서 뺀다.</summary>
        public enum Spot { Order, BeforeRoll, AfterRoll, PreDamage, AfterDamage, BattleEnd, MapOther, Own }

        /// <summary>자리: Att = 배틀 턴을 가진 쪽 · Def = 받는 쪽 · Side = 배틀 양쪽 어느 쪽이든 · Out = 배틀 밖 좌석 · Actor = 맵 행동하는 좌석 · Other = 남의 행동에 반응하는 좌석.</summary>
        public enum Role { Att, Def, Side, Out, Actor, Other }

        /// <summary>
        /// 카드 능력(키.B|R) → 쓰는 창 · 자리(하나 이상). 코드의 훅 자리(TrainerRules · BattleFlow)에서 옮겨 적었다 — 상시 보정은 그 보정이 듣는 창.
        /// 빈 배열 = Own(내 행동 안에서만).
        /// </summary>
        static readonly Dictionary<string, (Spot, Role)[]> Card = new Dictionary<string, (Spot, Role)[]>
        {
            { "ace_trainer.B", Own() }, { "ace_trainer.R", new[] { (Spot.Order, Role.Side) } },
            { "aroma_lady.B", new[] { (Spot.AfterRoll, Role.Att) } }, { "aroma_lady.R", new[] { (Spot.AfterDamage, Role.Att) } }, // 회복은 공격 피해와 같은 버퍼로 든다
            { "artist.B", new[] { (Spot.BattleEnd, Role.Side) } }, { "artist.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "backpacker.B", new[] { (Spot.BattleEnd, Role.Side) } }, { "backpacker.R", Own() },
            { "beauty.B", new[] { (Spot.PreDamage, Role.Att) } }, { "beauty.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "bird_keeper.B", Own() }, { "bird_keeper.R", new[] { (Spot.PreDamage, Role.Def) } },
            { "black_belt.B", new[] { (Spot.BattleEnd, Role.Side) } }, { "black_belt.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "bug_catcher.B", new[] { (Spot.BattleEnd, Role.Side) } }, { "bug_catcher.R", new[] { (Spot.Order, Role.Side), (Spot.PreDamage, Role.Att) } },
            { "chef.B", Own() }, { "chef.R", new[] { (Spot.BeforeRoll, Role.Att) } },
            { "collector.B", new[] { (Spot.BattleEnd, Role.Side), (Spot.MapOther, Role.Actor) } }, { "collector.R", Own() },
            { "delinquent.B", new[] { (Spot.MapOther, Role.Other) } }, { "delinquent.R", new[] { (Spot.PreDamage, Role.Def) } },
            { "diamond_acolyte.B", new[] { (Spot.AfterRoll, Role.Side) } }, { "diamond_acolyte.R", new[] { (Spot.Order, Role.Side) } },
            { "dragon_tamer.B", Own() }, { "dragon_tamer.R", new[] { (Spot.Order, Role.Side), (Spot.PreDamage, Role.Att) } },
            { "essentia.B", new[] { (Spot.BattleEnd, Role.Side) } }, { "essentia.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "expert.B", Own() }, { "expert.R", new[] { (Spot.AfterDamage, Role.Att) } },
            { "fairy_tale_girl.B", new[] { (Spot.AfterDamage, Role.Side) } }, { "fairy_tale_girl.R", new[] { (Spot.AfterDamage, Role.Side) } },
            { "fisherman.B", new[] { (Spot.AfterRoll, Role.Att) } }, { "fisherman.R", Own() },
            { "gardener.B", Own() }, { "gardener.R", new[] { (Spot.Order, Role.Side) } },
            { "gentleman.B", new[] { (Spot.BattleEnd, Role.Side) } }, { "gentleman.R", Own() },
            { "ginkgo_merchant.B", new[] { (Spot.MapOther, Role.Other) } }, { "ginkgo_merchant.R", new[] { (Spot.BeforeRoll, Role.Att) } },
            { "guitarist.B", new[] { (Spot.AfterRoll, Role.Att) } }, { "guitarist.R", new[] { (Spot.AfterRoll, Role.Att) } },
            { "hex_maniac.B", new[] { (Spot.PreDamage, Role.Att) } }, { "hex_maniac.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "hiker.B", Own() }, { "hiker.R", new[] { (Spot.Order, Role.Side) } },
            { "interviewers.B", new[] { (Spot.BattleEnd, Role.Out), (Spot.MapOther, Role.Other) } }, { "interviewers.R", Own() },
            { "kindler.B", Own() }, { "kindler.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "lady.B", Own() }, { "lady.R", Own() },
            { "ninja_boy.B", new[] { (Spot.Order, Role.Side) } }, { "ninja_boy.R", new[] { (Spot.Order, Role.Side) } },
            { "parasol_lady.B", new[] { (Spot.PreDamage, Role.Att) } }, { "parasol_lady.R", new[] { (Spot.PreDamage, Role.Def) } },
            { "pearl_acolyte.B", Own() }, { "pearl_acolyte.R", new[] { (Spot.Order, Role.Side) } },
            { "picnicker.B", Own() }, { "picnicker.R", Own() },
            { "platinum_acolyte.B", new[] { (Spot.BeforeRoll, Role.Att) } }, { "platinum_acolyte.R", new[] { (Spot.Order, Role.Side) } },
            { "poke_fan.B", new[] { (Spot.PreDamage, Role.Def) } }, { "poke_fan.R", Own() },
            { "poke_maniac.B", Own() }, { "poke_maniac.R", new[] { (Spot.PreDamage, Role.Def) } },
            { "pokemon_breeder.B", Own() }, { "pokemon_breeder.R", new[] { (Spot.Order, Role.Side) } },
            { "pokemon_ranger.B", Own() }, { "pokemon_ranger.R", new[] { (Spot.Order, Role.Side) } },
            { "psychic.B", Own() }, { "psychic.R", new[] { (Spot.AfterRoll, Role.Att) } },
            { "roller_skater.B", new[] { (Spot.AfterDamage, Role.Att) } }, { "roller_skater.R", new[] { (Spot.PreDamage, Role.Def) } },
            { "ruin_maniac.B", Own() }, { "ruin_maniac.R", new[] { (Spot.PreDamage, Role.Att), (Spot.BattleEnd, Role.Side) } },
            { "scientist.B", Own() }, { "scientist.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "scuba_diver.B", Own() }, { "scuba_diver.R", new[] { (Spot.AfterDamage, Role.Att) } },
            { "sky_trainer.B", Own() }, { "sky_trainer.R", new[] { (Spot.PreDamage, Role.Att) } },
            { "swimmer.B", new[] { (Spot.Order, Role.Side) } }, { "swimmer.R", new[] { (Spot.BeforeRoll, Role.Att) } },
            { "team_aqua.B", Own() }, { "team_aqua.R", new[] { (Spot.BeforeRoll, Role.Side), (Spot.PreDamage, Role.Side) } },
            { "team_magma.B", Own() }, { "team_magma.R", new[] { (Spot.BeforeRoll, Role.Side), (Spot.PreDamage, Role.Side) } },
            { "team_rocket.B", Own() }, { "team_rocket.R", new[] { (Spot.AfterDamage, Role.Side), (Spot.BattleEnd, Role.Side) } },
            { "tourist.B", Own() }, { "tourist.R", Own() },
            { "triathlete.B", Own() }, { "triathlete.R", new[] { (Spot.BeforeRoll, Role.Att) } },
            { "undivided_acolyte.B", Own() }, { "undivided_acolyte.R", new[] { (Spot.AfterDamage, Role.Att) } },
            { "veteran.B", Own() }, { "veteran.R", new[] { (Spot.AfterRoll, Role.Att) } },
            { "worker.B", Own() }, { "worker.R", new[] { (Spot.AfterDamage, Role.Side) } },
            { "youngster.B", new[] { (Spot.Order, Role.Side) } }, { "youngster.R", Own() },
        };

        static (Spot, Role)[] Own() => new (Spot, Role)[0];

        /// <summary>운명 카드 · 지닌물건(데이터 키) → 창 · 자리. 운명은 디아루가(굴린 뒤, 양쪽) · 기라티나(굴리기 전, 공격하는 쪽)만 배틀 창이다 — 펄기아 · 아르세우스는 내 맵 행동.</summary>
        static readonly Dictionary<string, (Spot, Role)[]> Other = new Dictionary<string, (Spot, Role)[]>
        {
            { "fate.dialga", new[] { (Spot.AfterRoll, Role.Side) } },
            { "fate.giratina", new[] { (Spot.BeforeRoll, Role.Att) } },
            { "item.swiftness_sash", new[] { (Spot.Order, Role.Side) } },
            { "item.choice_band", new[] { (Spot.PreDamage, Role.Att) } },
            { "item.lucky_egg", new[] { (Spot.AfterRoll, Role.Att) } },
            { "item.eviolite", new[] { (Spot.AfterRoll, Role.Def) } },
            { "item.star_shield", new[] { (Spot.PreDamage, Role.Def) } },
            { "item.blast_crystal", new[] { (Spot.PreDamage, Role.Def) } },
            { "item.substitute", new[] { (Spot.PreDamage, Role.Def) } },
        };

        public sealed class Source
        {
            public string Key;
            public Spot Spot;
            public Role Role;
        }

        /// <summary>데이터의 트레이너 카드 능력을 모두 훑어 분류표와 맞춘다 — 분류가 없는 능력 · 데이터에 없는 분류 키는 missing에.</summary>
        public static List<Source> Sources(Ctx ctx, List<string> missing)
        {
            var r = new List<Source>();
            var seen = new HashSet<string>();
            foreach (var card in ctx.Cards.TrainerCards)
                foreach (var ab in card.Abilities)
                {
                    string key = $"{card.Key}.{(ab.Color == AbilityColor.Blue ? "B" : "R")}";
                    seen.Add(key);
                    if (!Card.TryGetValue(key, out var spots)) { missing.Add("분류 없음 " + key); continue; }
                    foreach (var (spot, role) in spots) r.Add(new Source { Key = key, Spot = spot, Role = role });
                }
            foreach (var key in Card.Keys)
                if (!seen.Contains(key)) missing.Add("데이터에 없는 분류 " + key);
            foreach (var kv in Other)
            {
                if (kv.Key.StartsWith("item.") && Scene.Item(ctx, kv.Key) < 0) missing.Add("데이터에 없는 아이템 " + kv.Key);
                foreach (var (spot, role) in kv.Value) r.Add(new Source { Key = kv.Key, Spot = spot, Role = role });
            }
            return r;
        }

        /// <summary>두 자리가 다른 좌석으로 같은 순간에 설 수 있나(Side는 공격 · 받는 쪽 어느 쪽이든).</summary>
        static bool Apart(Role a, Role b)
        {
            bool Battle(Role x) => x == Role.Att || x == Role.Def || x == Role.Side;
            if (Battle(a) && Battle(b)) return !(a == b && a != Role.Side);
            if (a == Role.Out || b == Role.Out) return a != b && (Battle(a) || Battle(b));
            if (a == Role.Other && b == Role.Other) return true;
            return (a == Role.Actor && b == Role.Other) || (a == Role.Other && b == Role.Actor);
        }

        /// <summary>창마다 다른 좌석끼리 같은 순간에 쓸 수 있는 후보 쌍(키 정렬 「A + B」). 같은 카드 둘은 뺀다(카드는 한 장씩).</summary>
        public static Dictionary<Spot, HashSet<string>> Candidates(List<Source> sources)
        {
            var r = new Dictionary<Spot, HashSet<string>>();
            for (int i = 0; i < sources.Count; i++)
                for (int j = i; j < sources.Count; j++)
                {
                    var a = sources[i];
                    var b = sources[j];
                    if (a.Spot != b.Spot || a.Spot == Spot.Own || !Apart(a.Role, b.Role)) continue;
                    if (a.Key == b.Key && !a.Key.StartsWith("fate.") && !a.Key.StartsWith("item.")) continue;
                    if (!r.TryGetValue(a.Spot, out var set)) r[a.Spot] = set = new HashSet<string>();
                    set.Add(PairKey(a.Key, b.Key));
                }
            return r;
        }

        public static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? a + " + " + b : b + " + " + a;

        /// <summary>창마다 후보 쌍 수 · 장면이 덮은 쌍 수(덮은 쌍 목록).</summary>
        public static string Coverage(Ctx ctx, out int candidates, out int covered)
        {
            var missing = new List<string>();
            var cand = Candidates(Sources(ctx, missing));
            var all = new HashSet<string>();
            foreach (var set in cand.Values) all.UnionWith(set);
            var rows = new Dictionary<Spot, HashSet<string>>();
            var extra = new List<string>();
            foreach (var row in Rows())
            {
                string key = PairKey(row.A, row.B);
                if (!all.Contains(key))
                {
                    if (!extra.Contains(key)) extra.Add(key);
                    continue;
                }
                foreach (var kv in cand)
                    if (kv.Value.Contains(key))
                    {
                        if (!rows.TryGetValue(kv.Key, out var set)) rows[kv.Key] = set = new HashSet<string>();
                        set.Add(key);
                    }
            }
            var sb = new StringBuilder();
            candidates = covered = 0;
            foreach (Spot spot in Enum.GetValues(typeof(Spot)))
            {
                if (spot == Spot.Own) continue;
                int n = cand.TryGetValue(spot, out var c) ? c.Count : 0;
                var got = rows.TryGetValue(spot, out var g) ? g : new HashSet<string>();
                int hit = 0;
                foreach (var p in got)
                    if (c != null && c.Contains(p)) hit++;
                candidates += n;
                covered += hit;
                sb.Append($"{spot}: 후보 {n} · 장면 {hit} ({string.Join(", ", got)})\n");
                if (c != null)
                {
                    var rest = new List<string>();
                    foreach (var p in c)
                        if (!got.Contains(p)) rest.Add(p);
                    rest.Sort(string.CompareOrdinal);
                    sb.Append($"  {spot} 장면 밖: {string.Join(", ", rest)}\n");
                }
            }
            sb.Append($"후보 밖 장면(같은 좌석 · 창을 건너는 조건): {string.Join(", ", extra)}\n");
            return sb.ToString();
        }

        // ── 장면 표 ──

        public enum Use { Both, OnlyA, OnlyB }

        /// <summary>장면 하나의 결과: Miss = 기대와 다른 것(비면 맞음), Seen = 실제 요약(보고 줄).</summary>
        public sealed class Outcome
        {
            public readonly List<string> Miss = new List<string>();
            public readonly List<string> Seen = new List<string>();

            public void Expect(bool ok, string what)
            {
                if (!ok) Miss.Add(what);
            }

            public void Note(string what) => Seen.Add(what);
        }

        public sealed class Row
        {
            public string Id, A, B, Rule, Expect;
            public Spot Spot;
            public Func<Ctx, Use, Outcome> Run;
            public override string ToString() => Id;
        }

        /// <summary>
        /// 찾은 것(장면 id_변형 → 무엇): 기대(rulings 근거)와 실제가 다르다 — 고치지 않는다(목록). 시험은 여기 있는 것이 아직 재현되는지 본다(고치면 줄을 지운다).
        /// </summary>
        public static readonly Dictionary<string, string> Known = new Dictionary<string, string>
        {
            // K1(고침, 11-20): C17_Both — 한 명령에 두 좌석 스탬프면 interviewers B가 좌석 번호 작은 쪽 하나만 물었다 → Sweep은 G6 맵 창 순서 · 둘째는 ScoopNext 줄로 차례로 묻는다.
            // K2(고침, 사용자 결정 135): C18_Both — kindler R의 2 피해가 BattleFlow.Buffer로 바로 들어가 받는 쪽 카드를 지나지 않았다 → TrainerRules.SideDamage로(기술 효과 고정 피해와 같은 길).
        };

        /// <summary>대표 쌍의 장면. A · B = 출처 키(<see cref="Sources"/>와 같은 꼴). 변형 = 둘 다 · A만 · B만(쓰는 출처는 「안 함」으로, 상시 출처는 빼서).</summary>
        public static IEnumerable<Row> Rows()
        {
            yield return new Row { Id = "C01", Spot = Spot.BeforeRoll, A = "swimmer.R", B = "parasol_lady.R", Run = SwimmerParasol,
                Rule = "A34 swimmer R · A41 parasol_lady R · G6(굴리기 전: 공격하는 쪽의 카드 물음 → 피해 셈)",
                Expect = "둘 다: 물 기술이 벌레가 돼 비의 −2가 빠진다(뒤가 조건을 잃음) · A만: 벌레 · B만: 물 그대로 −2" };
            yield return new Row { Id = "C02", Spot = Spot.BeforeRoll, A = "swimmer.R", B = "team_aqua.R", Run = SwimmerAqua,
                Rule = "A34 swimmer R · A32 team_aqua R(배틀 양쪽의 물 기술 +1, 원작 커뮤니티 확인)",
                Expect = "둘 다: 타입을 바꾸면 받는 쪽 team_aqua R의 물 기술 +1(주사위 1개)이 빠진다 · A만: 바뀐 타입 · B만: 물 기술 +1" };
            yield return new Row { Id = "C03", Spot = Spot.BeforeRoll, A = "chef.R", B = "fate.giratina", Run = ChefGiratina,
                Rule = "A34 chef R · A24 ③ 기라티나(공격하는 쪽만 — 받는 쪽 굴리기 전 창은 「안 씀」뿐) · G6",
                Expect = "둘 다: 카드 물음 → 굴리기 전 창, 공격력 +1(마스터워크) +1(기라티나) · 받는 쪽은 기라티나를 쥐어도 못 낸다 · A만 / B만: 그 하나" };
            yield return new Row { Id = "C04", Spot = Spot.AfterRoll, A = "veteran.R", B = "fate.dialga", Run = VeteranDialga,
                Rule = "A34 veteran R(굴린 바로 뒤, 면 바꾸기 전) · A24 ① 디아루가(굴린 뒤 창) · G14 「더한 주사위」",
                Expect = "둘 다: 심볼 2개를 더한 굴림에 받는 쪽 디아루가 — 더한 심볼 자리도 다시 굴릴 수 있다(G14 꼴) · 카드 물음은 창보다 앞이라 둘째 바퀴가 없다 · A만 / B만: 그 하나" };
            yield return new Row { Id = "C05", Spot = Spot.AfterRoll, A = "guitarist.B", B = "fate.dialga", Run = GuitaristDialga,
                Rule = "A41 guitarist B(결정 134 — 굴린 뒤 반응 창 안, 디아루가와 같은 바퀴) · A24 ① 디아루가",
                Expect = "둘 다: 앰프는 디아루가 전 굴림의 심볼로 오르고 쓸지도 그 전에 묻는다(디아루가로 심볼이 사라져도 앰프는 그대로) · A만 / B만: 그 하나" };
            yield return new Row { Id = "C06", Spot = Spot.AfterRoll, A = "fate.dialga", B = "fate.dialga", Run = DialgaDialga,
                Rule = "G6(한꺼번에 묻고 공격하는 쪽부터 · 누가 쓰면 한 바퀴 더 · 안 썼던 좌석도 다시) · A11(배틀 턴에 1번)",
                Expect = "둘 다(같은 바퀴): 공격 쪽 → 받는 쪽 순서 · 둘째 바퀴에 낼 사람 없음 · A만: 받는 쪽에게 한 바퀴 더 묻는다 · B만: 공격 쪽이 둘째 바퀴에 받아친다" };
            yield return new Row { Id = "C07", Spot = Spot.AfterRoll, A = "veteran.R", B = "item.lucky_egg", Run = VeteranEgg,
                Rule = "A34 veteran R(「더한 심볼은 … 면 바꾸기에 그대로 든다」) · S10 행운의 알",
                Expect = "둘 다: 더한 심볼 2개를 행운의 알이 크리티컬 1개로 · A만: 심볼 2개 그대로 · B만: 바꿀 심볼 없음" };
            yield return new Row { Id = "C08", Spot = Spot.PreDamage, A = "kindler.R", B = "item.star_shield", Run = KindlerShield,
                Rule = "A35 kindler R · S10 별 방패(첫 공격 −2 · 그 턴 상태이상 면역)",
                Expect = "둘 다: 화상은 막히고(장작은 쓴다) 2 피해는 들어간다 · 공격 피해 −2 · A만: 화상 + 2 · B만: −2만" };
            yield return new Row { Id = "C09", Spot = Spot.PreDamage, A = "kindler.R", B = "item.substitute", Run = KindlerSubstitute,
                Rule = "A35 kindler R · A41 poke_maniac R 대타출동(피해 직전, [가정 §5 #147 ⑥] 피해를 그 포켓몬으로 다시 센다)",
                Expect = "둘 다: 대타출동으로 들어온 포켓몬이 공격 피해와 화상 + 2를 받는다 · A만: 처음 포켓몬 · B만: 대타가 공격 피해만" };
            yield return new Row { Id = "C18", Spot = Spot.PreDamage, A = "kindler.R", B = "delinquent.R", Run = KindlerDelinquent,
                Rule = "A35 kindler R(「deal 2 damage」 — 그 턴 버퍼) · A32 delinquent R(「take 2 less damage from attacks」) · A32 [가정] 기술 효과의 고정 피해도 받는 쪽 카드를 지난다(TrainerRules.SideDamage — 원작 커뮤니티 확인)",
                Expect = "둘 다: 공격 피해 −2, kindler 2 피해도 「공격에서 받는 피해」라 −2(→ 0 — 기술 효과 피해와 같은 꼴) · A만: 2 그대로 · B만: 공격 피해 −2만" };
            yield return new Row { Id = "C10", Spot = Spot.PreDamage, A = "black_belt.R", B = "roller_skater.R", Run = BlackBeltSkater,
                Rule = "A35 black_belt R(화상 격투 = 총 피해 +2) · A32 roller_skater R(한 공격 최대 6) · 01 §6-4 ⑤ → ⑦",
                Expect = "둘 다: +2를 더한 뒤 상한 6 · A만: 상한 없음 · B만: 화상 −2 뒤 상한" };
            yield return new Row { Id = "C11", Spot = Spot.AfterDamage, A = "undivided_acolyte.R", B = "team_rocket.R", Run = AcolyteRocket,
                Rule = "A35 undivided_acolyte R(공격 뒤 체력 2 이하 기절) · A35 team_rocket R(내 포켓몬이 기절하면 상대에게 2)",
                Expect = "둘 다: 카드로 기절 → 받는 쪽 카드가 공격한 포켓몬에게 2 · A만: 기절만 · B만: 기절 없음 → 2도 없음" };
            yield return new Row { Id = "C12", Spot = Spot.AfterDamage, A = "aroma_lady.R", B = "team_rocket.R", Run = AromaRocket,
                Rule = "A35 aroma_lady R(준 피해 절반 올림 회복 — 공격 피해와 같은 버퍼) · A35 team_rocket R(기절한 뒤 2) · p.13 동시 적용",
                Expect = "둘 다: 회복이 공격 피해와 함께 먼저 들고 기절 뒤 2 — 체력 1이던 공격자가 산다 · A만: 회복만 · B만: 2로 쓰러진다" };
            yield return new Row { Id = "C13", Spot = Spot.AfterDamage, A = "scuba_diver.R", B = "worker.R", Run = ScubaWorker,
                Rule = "A35 scuba_diver R(물 기술 뒤 교체 + 상태이상 회복) · A35 worker R(강철이 나오면 상대 피로 +3 · 교체 금지) · S7_5(기절하면 상대가 먼저 교체)",
                Expect = "둘 다: 공격 쪽 교체(독 풀림)가 먼저 → 받는 쪽 강철이 나와 새로 나온 포켓몬에 피로 +3 · 교체 금지 · A만: 교체만 · B만: 처음 포켓몬에 worker" };
            yield return new Row { Id = "C14", Spot = Spot.Order, A = "ninja_boy.B", B = "gardener.R", Run = NinjaGardener,
                Rule = "A34 ninja_boy B·R(토큰으로 우선도와 상관없이 · 쓰면 독·혼란·졸림 강제) · A32 gardener R 우선도 +3",
                Expect = "둘 다: 느린 쪽 토큰으로 선턴 + Toxin Dart · 토큰은 상대에게 · A만: 동률 토큰(빨강만) · B만: 빠른 쪽 선턴" };
            yield return new Row { Id = "C15", Spot = Spot.BattleEnd, A = "team_rocket.R", B = "backpacker.B", Run = RocketBackpacker,
                Rule = "A35 team_rocket R(바닥나면 갈 칸 — 배틀 닫기 전) · A38 backpacker B(배틀 뒤 턴 끝 앞) · rulings 1328 배틀 뒤 카드 물음 순서",
                Expect = "둘 다: 진 쪽 갈 칸 → 이긴 쪽 보상 → 이긴 쪽 회복(2 피해까지) · A만: 회복 없음 · B만: 진 쪽은 고향으로" };
            yield return new Row { Id = "C16", Spot = Spot.BattleEnd, A = "backpacker.B", B = "interviewers.B", Run = BackpackerInterviewers,
                Rule = "A38 backpacker B · A38 interviewers B(「도전 보호 중인 좌석에는 묻지 않는다」) · p.9 트레이너전 뒤 도전 보호 · rulings 1328(배틀 쪽 먼저 → 배틀 밖)",
                Expect = "둘 다: 이긴 좌석 회복만 — 승리 스탬프로 남은 인터뷰어 표시는 도전 보호로 지워져 묻지 않는다(뒤가 조건을 잃음) · A만(인터뷰어 없음): 회복 · B만(배낭 없음): 아무것도 묻지 않음" };
            yield return new Row { Id = "C17", Spot = Spot.MapOther, A = "ginkgo_merchant.B", B = "interviewers.B", Run = GinkgoInterviewers,
                Rule = "A38 ginkgo_merchant B · A38 interviewers B · G6 맵 창(차례 좌석부터 시계 방향) · A30 상태 조건 스탬프(명령 끝)",
                Expect = "둘 다: 은행 거래 한 번에 거래한 좌석 · ginkgo 좌석이 같이 스탬프 — 인터뷰어는 차례 좌석부터 묻는다 · A만: ginkgo 좌석만 · B만: 거래한 좌석만" };
            yield return new Row { Id = "C19", Spot = Spot.MapOther, A = "collector.B", B = "interviewers.B", Run = CollectorInterviewers,
                Rule = "A39 collector B(스탬프 보상 두 번 — 묻지 않음 [가정 §5 #145 ④]) · A38 interviewers B · A30 상태 조건 스탬프(명령 끝)",
                Expect = "둘 다: 거래한 좌석이 일곱 색 스탬프 · 수호신의 서 2권 · 인터뷰어 물음 · A만: 2권 · 물음 없음 · B만: 1권 · 물음" };
        }

        // ── 장면 틀 ──

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        const string Garchomp = "445:0", Bidoof = "399:0", Turtwig = "387:0", Torterra = "389:1", Piplup = "393:0", Bastiodon = "411:0",
            Monferno = "391:0", Lucario = "448:0", Umbreon = "197:0";

        sealed class Arena
        {
            public Ctx Ctx;
            public GameState S;
            public EventLog Log = new EventLog();
            public List<int> Mine = new List<int>(), Theirs = new List<int>();
            public int From;
            /// <summary>답한 선택의 차례(좌석:종류/갈래:출처 종류).</summary>
            public readonly List<string> Asked = new List<string>();
            public int A => Mine[0];
            public int D => Theirs[0];
            public List<GameEvent> Events => Log.Events.GetRange(From, Log.Events.Count - From);
            public SourceRef CardOf(int seat) => new SourceRef(SourceKind.TrainerCard, S.Seats[seat].TrainerCard);
            public int Hp(int p) => PokemonRules.Hp(Ctx, S.Pokemon[p]);
            public int Max(int p) => PokemonRules.MaxHp(Ctx, S.Pokemon[p]);
        }

        static void GiveCard(Ctx ctx, GameState s, int seat, string key)
        {
            int card = ctx.Cards.TrainerCardIds.IndexOf(key);
            Assert.GreaterOrEqual(card, 0, key);
            s.Seats[seat].TrainerCard = card;
            s.Seats[seat].TrainerCardTier = TrainerRules.Gold;
        }

        /// <summary>
        /// 트레이너전: 좌석 0(공격하는 쪽)의 mine 대 좌석 1의 theirs, 좌석 2는 배틀 밖. cards = 좌석 0 · 1 · 2의 카드(null = 없음, 골드). prep = 배틀 전에 손볼 것.
        /// settle이면 우선권 토큰(동률)을 좌석 0이 써서 좌석 0의 기술 고르기가 열려 있다.
        /// </summary>
        static Arena Field(Ctx ctx, string[] cards, string[] mine, string[] theirs, Action<Arena> prep = null, bool settle = true, string outside = Bidoof)
        {
            var t = new Arena { Ctx = ctx, S = Battles.Game(ctx, 3) };
            foreach (var k in mine) t.Mine.Add(Scene.Give(t.S, ctx, 0, k));
            foreach (var k in theirs) t.Theirs.Add(Scene.Give(t.S, ctx, 1, k));
            if (outside != null) Scene.Give(t.S, ctx, 2, outside);
            for (int seat = 0; seat < cards.Length; seat++)
                if (cards[seat] != null) GiveCard(ctx, t.S, seat, cards[seat]);
            for (int i = 0; i < SeatState.StampCount; i++) t.S.Seats[0].Stamps[i] = t.S.Seats[1].Stamps[i] = i != StampRules.TrainerWin; // 규토리 · 지닌물건 스탬프가 장면 가운데 끼지 않게
            prep?.Invoke(t);
            TrainerRules.Refresh(t.S, ctx);
            t.From = t.Log.Events.Count;
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            for (int guard = 0; guard < 4 && t.S.Pending.Count == 1 && t.S.Pending[0].Kind == ChoiceKind.SendIn; guard++)
            {
                var c = t.S.Pending[0];
                Battles.Answer(t.S, ctx, c, ChoiceAnswer.Pick(c.Seat == 0 ? t.A : t.D), t.Log);
            }
            if (!settle) return t;
            if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0이 먼저");
            return t;
        }

        static ulong SeedFor(AttackDieFace[] faces)
        {
            for (ulong seed = 0; seed < 4000000; seed++)
            {
                var roll = AttackDice.Roll(new Pcg32(seed), faces.Length);
                bool same = true;
                for (int i = 0; i < roll.Length && same; i++) same = roll[i] == faces[i];
                if (same) return seed;
            }
            Assert.Fail("그 굴림이 나오는 시드가 없다");
            return 0;
        }

        /// <summary>차례인 좌석이 그 기술(배운 기술 칸 0에 넣는다)을 쓴다 — 굴림은 faces. 이벤트는 여기부터.</summary>
        static void Attack(Arena t, string move, params AttackDieFace[] faces)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            int active = t.S.Battle.Sides[ch.Seat].Active;
            int m = Scene.Move(t.Ctx, move);
            Assert.GreaterOrEqual(m, 0, move);
            if (t.Ctx.Cards.Pokemon[t.S.Pokemon[active].Def].Signature != m) t.S.Pokemon[active].Moves[0] = m;
            if (faces.Length > 0) t.S.Rng = new Pcg32(SeedFor(faces));
            t.From = t.Log.Events.Count;
            Battles.Answer(t.S, t.Ctx, ch, Battles.Move(m), t.Log);
        }

        /// <summary>
        /// 기술 고르기 · 맵 행동이 다시 열릴 때까지(또는 limit) 열린 선택에 답한다: pick이 답을 내면 그것, null이면 기본 답. 답한 선택은 Asked에 적는다.
        /// </summary>
        static void Resolve(Arena t, Func<PendingChoice, List<ChoiceAnswer>, ChoiceAnswer> pick, int limit = 40)
        {
            for (int i = 0; i < limit; i++)
            {
                var c = t.S.Pending.Find(x => !x.Answered);
                if (c == null || c.Kind == ChoiceKind.BattleAction || c.Kind == ChoiceKind.MapAction) return;
                var legal = Scene.Legal(t.S, t.Ctx, c);
                var a = pick?.Invoke(c, legal) ?? c.Default.Clone();
                t.Asked.Add($"{c.Seat}:{c.Kind}/{c.Sub}:{c.Source.Kind}");
                Battles.Answer(t.S, t.Ctx, c, a, t.Log);
            }
            Assert.Fail("선택이 끝나지 않는다");
        }

        static bool FromCard(Arena t, PendingChoice c, int seat) => c.Seat == seat && c.Source.Equals(t.CardOf(seat)) && t.S.Seats[seat].TrainerCard >= 0;

        static ChoiceAnswer Decline(List<ChoiceAnswer> legal) => legal.Find(a => a.Kind == AnswerKind.Decline);

        static ChoiceAnswer Fate(List<ChoiceAnswer> legal, FateCard card, int die = -1) =>
            legal.Find(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)card && a.Arg == 1 && (die < 0 || (a.Values.Count > 0 && a.Values[0] == die)));

        static void Hand(Ctx ctx, GameState s, int seat, params FateCard[] cards)
        {
            s.Seats[seat].FateHand.Clear();
            foreach (var c in cards) s.Seats[seat].FateHand.Add(FateRules.CardKey(ctx, c));
        }

        static bool Has(IReadOnlyList<ApricornColor> list, ApricornColor c)
        {
            foreach (var x in list)
                if (x == c) return true;
            return false;
        }

        static List<T> All<T>(List<GameEvent> events) where T : GameEvent
        {
            var r = new List<T>();
            foreach (var e in events)
                if (e is T x) r.Add(x);
            return r;
        }

        static string Used(Arena t)
        {
            var parts = new List<string>();
            foreach (var e in t.Events)
            {
                if (e is AbilityUsed au) parts.Add($"{au.Seat}:{t.Ctx.Cards.TrainerCards[au.CardKey].Key}.{(au.Ability == AbilityColor.Blue ? "B" : "R")}");
                else if (e is FatePlayed fp) parts.Add($"{fp.Seat}:fate.{fp.Card.ToString().ToLowerInvariant()}");
            }
            return string.Join(" → ", parts);
        }

        static DamageBreakdown Breakdown(Arena t, int target = -1)
        {
            var l = All<DamageBreakdown>(t.Events);
            return l.Find(x => target < 0 || x.TargetId == target);
        }

        static bool HasStep(DamageBreakdown d, DamageStepKind kind, SourceRef src) =>
            d != null && Array.Exists(d.Steps, x => x.Kind == kind && x.Source.Equals(src));

        // ── 굴리기 전 ──

        static Outcome SwimmerParasol(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "swimmer", u == Use.OnlyA ? null : "parasol_lady", null }, new[] { Piplup }, new[] { Bastiodon },
                a => a.S.Weather = Array.IndexOf(TrainerRules.WeatherTypes, PokemonType.Water));
            Attack(t, "water_gun", R, R);
            Resolve(t, (c, legal) => FromCard(t, c, 0) ? (u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Pick((int)PokemonType.Bug)) : null);
            var moved = All<MoveUsed>(t.Events).Find(e => e.Side == 0);
            var d = Breakdown(t, t.D);
            bool guarded = u != Use.OnlyA && HasStep(d, DamageStepKind.Reduction, t.CardOf(1));
            o.Note($"기술 타입 {moved?.Type} · 비의 −2 {(guarded ? "있음" : "없음")} · 쓴 것 {Used(t)}");
            o.Expect(moved != null && moved.Type == (u == Use.OnlyB ? PokemonType.Water : PokemonType.Bug), "기술 타입");
            o.Expect(guarded == (u == Use.OnlyB), "parasol_lady R −2는 비 타입(물) 기술일 때만 — 벌레로 바뀌면 빠진다");
            return o;
        }

        static Outcome SwimmerAqua(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "swimmer", u == Use.OnlyA ? null : "team_aqua", null }, new[] { Piplup }, new[] { Bastiodon });
            Attack(t, "water_gun", R, R, R);
            Resolve(t, (c, legal) => FromCard(t, c, 0) ? (u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Pick((int)PokemonType.Bug)) : null);
            var roll = All<DiceRolled>(t.Events).Find(e => e.Side == 0);
            bool aqua = roll != null && roll.PowerMods != null && Array.Exists(roll.PowerMods, m => m.Source.Equals(t.CardOf(1)));
            o.Note($"굴린 주사위 {roll?.Faces?.Length} · team_aqua +1 {(aqua ? "있음" : "없음")} · 쓴 것 {Used(t)}");
            o.Expect(aqua == (u == Use.OnlyB), "받는 쪽 team_aqua R의 +1은 물 기술일 때만");
            return o;
        }

        static Outcome ChefGiratina(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { u == Use.OnlyB ? null : "chef", null, null }, new[] { Garchomp }, new[] { Garchomp }, a =>
            {
                for (int c = 0; c < SeatState.ApricornColors; c++) a.S.Seats[0].Apricorns[c] = 1;
                Hand(ctx, a.S, 0, u == Use.OnlyA ? new FateCard[0] : new[] { FateCard.Giratina });
                Hand(ctx, a.S, 1, FateCard.Giratina);
            });
            int hp = t.Hp(t.A);
            Attack(t, "dual_chop", R, R, R, R);
            bool defenderOnlyDecline = true;
            Resolve(t, (c, legal) =>
            {
                if (FromCard(t, c, 0)) return ChoiceAnswer.Of(AnswerKind.Accept);
                if (c.Sub == ChoiceSub.Fate && c.Seat == 1)
                {
                    if (c.Window == Window.BeforeRoll && legal.Exists(a => a.Kind == AnswerKind.PlayFate)) defenderOnlyDecline = false;
                    return Decline(legal);
                }
                if (c.Sub == ChoiceSub.Fate && c.Seat == 0) return c.Window == Window.BeforeRoll ? Fate(legal, FateCard.Giratina) ?? Decline(legal) : Decline(legal);
                return null;
            });
            var roll = All<DiceRolled>(t.Events).Find(e => e.Side == 0);
            bool chef = roll != null && Array.Exists(roll.PowerMods ?? new Mod[0], m => m.Source.Equals(t.CardOf(0)) && t.S.Seats[0].TrainerCard >= 0);
            bool gira = roll != null && Array.Exists(roll.PowerMods ?? new Mod[0], m => m.Source.Kind == SourceKind.Fate);
            o.Note($"쓴 것 {Used(t)} · 공격력 보정 마스터워크 {chef} · 기라티나 {gira} · 받는 쪽 굴리기 전 창 「안 씀」뿐 {defenderOnlyDecline}");
            o.Expect(chef == (u != Use.OnlyB), "마스터워크 +1");
            o.Expect(gira == (u != Use.OnlyA), "기라티나 +1");
            o.Expect(defenderOnlyDecline, "받는 쪽은 굴리기 전 창에서 기라티나를 못 낸다(p.19 「Before attacking」)");
            if (u == Use.Both) o.Expect(Used(t).StartsWith("0:chef.R → 0:fate.giratina"), "카드 물음(굴리기 전 자리) → 굴리기 전 창");
            if (u != Use.OnlyA) o.Expect(hp - t.Hp(t.A) >= FateRules.SacrificeDamage, "기라티나 자해 2");
            return o;
        }

        // ── 굴린 뒤 ──

        static Outcome VeteranDialga(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "veteran", null, null }, new[] { Garchomp }, new[] { Bastiodon }, a => Hand(ctx, a.S, 1, u == Use.OnlyA ? new FateCard[0] : new[] { FateCard.Dialga }));
            Attack(t, "dual_chop", R, R, R);
            int faces = -1, dialgaDice = -1, veteranAsks = 0, rolled = -1;
            Resolve(t, (c, legal) =>
            {
                if (FromCard(t, c, 0))
                {
                    veteranAsks++;
                    return u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Pick(2);
                }
                if (c.Sub == ChoiceSub.Fate && c.Seat == 1 && c.Window == Window.AfterRoll && u != Use.OnlyA)
                {
                    faces = t.S.Battle.Faces.Count;
                    rolled = All<DiceRolled>(t.Events).Find(e => e.Side == 0).Faces.Length;
                    dialgaDice = legal.FindAll(a => a.Kind == AnswerKind.PlayFate && a.Arg == 1).Count;
                    return Fate(legal, FateCard.Dialga, faces - 1) ?? Decline(legal);
                }
                return c.Sub == ChoiceSub.Fate ? Decline(legal) : null;
            });
            var changed = All<DiceFacesChanged>(t.Events);
            bool rerolledAdded = changed.Exists(e => e.Source.Kind == SourceKind.Fate && Array.Exists(e.Changes, x => x.Index >= 2));
            o.Note($"쓴 것 {Used(t)} · 디아루가 때 굴림 {faces}면 · 고를 수 있는 주사위 {dialgaDice} · 더한 심볼을 다시 굴림 {rerolledAdded} · veteran 물음 {veteranAsks}번");
            o.Expect(veteranAsks == 1, "veteran R은 공격마다 한 번(디아루가 뒤 다시 묻지 않는다 — 창 밖 물음)");
            if (u == Use.Both)
            {
                o.Expect(faces == rolled + 2, "심볼 2개가 더해진 굴림");
                o.Expect(dialgaDice == faces, "더한 심볼 자리도 디아루가가 고를 수 있다(G14 「더한 주사위」 꼴)");
                o.Expect(rerolledAdded, "더한 심볼 자리를 다시 굴렸다");
            }
            if (u == Use.OnlyB) o.Expect(faces == rolled, "veteran을 안 쓰면 굴림 그대로");
            return o;
        }

        static Outcome GuitaristDialga(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "guitarist", null, null }, new[] { Garchomp }, new[] { Bastiodon }, a =>
            {
                a.S.Seats[0].Amp = 2;
                Hand(ctx, a.S, 1, u == Use.OnlyA ? new FateCard[0] : new[] { FateCard.Dialga });
            });
            Attack(t, "slash", S, R, R, R);
            int ampAtAsk = -1, ampAtDialga = -1;
            bool ampUsed = false, dialgaUsed = false;
            var asked = new List<string>();
            Resolve(t, (c, legal) =>
            {
                if (c.Sub == ChoiceSub.Fate && c.Window == Window.AfterRoll)
                {
                    asked.Add($"fate{c.Seat}");
                    if (c.Seat == 0)
                    {
                        // 결정 134: 앰프는 굴린 뒤 반응 창 안의 공격하는 좌석 답(UseAbility · AmpTarget)
                        var amp = legal.Find(a => a.Kind == AnswerKind.UseAbility && a.Target == TrainerRules.AmpTarget);
                        if (ampAtAsk < 0 && amp != null) ampAtAsk = t.S.Seats[0].Amp;
                        if (u != Use.OnlyB && amp != null && !ampUsed) { ampUsed = true; return amp; }
                        return Decline(legal);
                    }
                    if (c.Seat == 1 && u != Use.OnlyA && !dialgaUsed) { dialgaUsed = true; ampAtDialga = t.S.Seats[0].Amp; return Fate(legal, FateCard.Dialga, 0) ?? Decline(legal); }
                }
                return c.Sub == ChoiceSub.Fate ? Decline(legal) : null;
            });
            var d = Breakdown(t, t.D);
            bool ampHit = d != null && Array.Exists(d.Steps, x => x.Kind == DamageStepKind.Effect && x.Source.Equals(t.CardOf(0)));
            o.Note($"물은 차례 {string.Join(" → ", asked)} · 물을 때 앰프 {ampAtAsk} · 디아루가 때 앰프 {ampAtDialga} · 남은 앰프 {t.S.Seats[0].Amp} · 앰프 피해 {ampHit} · 쓴 것 {Used(t)}");
            o.Expect(asked.Count > 0 && asked[0] == "fate0", "앰프는 굴린 뒤 반응 창 안 — 공격하는 좌석이 먼저(결정 134 · G6)");
            if (u == Use.Both) o.Expect(asked.Contains("fate1"), "디아루가와 같은 창(받는 쪽도 물음)");
            o.Expect(ampAtAsk == 3, "굴림의 심볼 1개로 앰프 2 → 3");
            o.Expect(ampHit == (u != Use.OnlyB), "쓰면 그 값이 효과 피해");
            if (u == Use.OnlyB) o.Expect(t.S.Seats[0].Amp == 3, "안 쓰면 앰프 3 그대로(디아루가로 심볼이 사라져도 내리지 않는다)");
            return o;
        }

        static Outcome DialgaDialga(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new string[] { null, null, null }, new[] { Garchomp }, new[] { Bastiodon }, a =>
            {
                Hand(ctx, a.S, 0, FateCard.Dialga);
                Hand(ctx, a.S, 1, FateCard.Dialga);
            });
            Attack(t, "slash", R, R, R, R);
            var rounds = new List<string>();
            Resolve(t, (c, legal) =>
            {
                if (c.Sub != ChoiceSub.Fate) return null;
                if (c.Window != Window.AfterRoll) return Decline(legal);
                var win = t.S.Windows.Count > 0 ? t.S.Windows[t.S.Windows.Count - 1] : null;
                int round = win != null ? win.Round : 0;
                bool can = legal.Exists(a => a.Kind == AnswerKind.PlayFate);
                ChoiceAnswer pick = Decline(legal);
                if (can && c.Seat == 0 && (u == Use.Both || u == Use.OnlyA || (u == Use.OnlyB && round == 2))) pick = Fate(legal, FateCard.Dialga, 0);
                if (can && c.Seat == 1 && (u == Use.Both || u == Use.OnlyB)) pick = Fate(legal, FateCard.Dialga, 1);
                rounds.Add($"r{round}:{c.Seat}{(can ? "" : "x")}{(pick != null && pick.Kind == AnswerKind.PlayFate ? "+" : "-")}");
                return pick;
            });
            string used = Used(t);
            o.Note($"바퀴 {string.Join(" ", rounds)} · 쓴 것 {used}");
            if (u == Use.Both)
            {
                o.Expect(used == "0:fate.dialga → 1:fate.dialga", "같은 바퀴 — 공격하는 쪽 먼저 적용(G6)");
                o.Expect(!rounds.Exists(r => r.StartsWith("r2") && !r.Contains("x")), "둘째 바퀴에 낼 수 있는 좌석이 없다(배틀 턴에 1번 — A11)");
            }
            if (u == Use.OnlyA)
            {
                o.Expect(used == "0:fate.dialga", "공격 쪽만");
                o.Expect(rounds.Exists(r => r.StartsWith("r2:1") && !r.Contains("x")), "받는 쪽은 둘째 바퀴에 다시 묻는다(안 썼던 좌석도 — G6)");
            }
            if (u == Use.OnlyB)
            {
                o.Expect(used == "1:fate.dialga → 0:fate.dialga", "받는 쪽이 먼저 쓰고 공격 쪽이 둘째 바퀴에 받아친다");
                o.Expect(!rounds.Exists(r => r.StartsWith("r3") && !r.Contains("x")), "셋째 바퀴에 낼 사람이 없다");
            }
            return o;
        }

        static Outcome VeteranEgg(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "veteran", null, null }, new[] { Garchomp }, new[] { Bastiodon }, a =>
            {
                if (u != Use.OnlyA) a.S.Pokemon[a.A].HeldItem = Scene.Item(ctx, "item.lucky_egg");
            });
            Attack(t, "dual_chop", R, R, R);
            Resolve(t, (c, legal) => FromCard(t, c, 0) ? (u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Pick(2)) : c.Sub == ChoiceSub.Fate ? Decline(legal) : null);
            var roll = All<DamageBreakdown>(t.Events).Find(x => x.TargetId == t.D);
            var eggs = All<DiceFacesChanged>(t.Events).FindAll(e => e.Source.Kind == SourceKind.Item); // op 둘(첫 심볼 → 크리티컬 · 다음 심볼 지움) = 이벤트 둘
            var egg = eggs.Count > 0 ? eggs[0] : null;
            var last = eggs.Count > 0 ? eggs[eggs.Count - 1] : null;
            int crit = last != null ? last.SrcAfter[2] : -1, sym = last != null ? last.SrcAfter[0] : -1;
            o.Note($"쓴 것 {Used(t)} · 행운의 알 {(egg != null ? $"심볼 {egg.SrcBefore[0]}→{sym} 크리티컬 {egg.SrcBefore[2]}→{crit}" : "없음")} · 피해 {roll?.Final}");
            if (u == Use.Both) o.Expect(egg != null && egg.SrcBefore[0] == 2 && sym == 0 && crit == 1, "더한 심볼 2개 → 크리티컬 1개");
            if (u == Use.OnlyA) o.Expect(egg == null, "알이 없다");
            if (u == Use.OnlyB) o.Expect(egg == null || egg.Changes.Length == 0, "바꿀 심볼이 없다(0 결과)");
            return o;
        }

        // ── 피해 직전 · 피해 ──

        static Outcome KindlerShield(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "kindler", null, null }, new[] { Monferno }, new[] { Bastiodon }, a =>
            {
                a.S.Seats[0].Firewood = 2;
                if (u != Use.OnlyA) a.S.Pokemon[a.D].HeldItem = Scene.Item(ctx, "item.star_shield");
            });
            int before = t.Hp(t.D);
            Attack(t, "flame_wheel", R, R, R);
            Resolve(t, (c, legal) => FromCard(t, c, 0) ? (u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Of(AnswerKind.Accept)) : c.Sub == ChoiceSub.Fate ? Decline(legal) : null);
            var d = Breakdown(t, t.D);
            bool shield = d != null && Array.Exists(d.Steps, x => x.Kind == DamageStepKind.Reduction && x.Source.Kind == SourceKind.Item);
            bool burned = StatusRules.Has(t.S.Pokemon[t.D], StatusEffect.Burned);
            int lost = before - t.Hp(t.D);
            o.Note($"쓴 것 {Used(t)} · 별 방패 −2 {shield} · 화상 {burned} · 공격 피해 {d?.Final} · 잃은 체력 {lost} · 장작 {t.S.Seats[0].Firewood}");
            o.Expect(shield == (u != Use.OnlyA), "별 방패 −2(첫 공격)");
            o.Expect(burned == (u == Use.OnlyA), "화상은 별 방패가 없을 때만(그 턴 상태이상 면역)");
            o.Expect(d != null && lost == d.Final + (u == Use.OnlyB ? 0 : TrainerRules.IgnitionDamage), "kindler 2 피해는 별 방패와 상관없이 따로 든다");
            o.Expect(t.S.Seats[0].Firewood == (u == Use.OnlyB ? 2 : 1), "쓰면 장작 1(화상이 막혀도)");
            return o;
        }

        static Outcome KindlerSubstitute(Ctx ctx, Use u)
        {
            var o = new Outcome();
            int sub = -1;
            var t = Field(ctx, new[] { "kindler", "poke_maniac", null }, new[] { Monferno }, new[] { Bastiodon, Garchomp }, a =>
            {
                a.S.Seats[0].Firewood = 2;
                a.S.Seats[1].OpenSlots = Math.Max(a.S.Seats[1].OpenSlots, 2);
                sub = a.Theirs[1];
                a.S.Pokemon[sub].HeldItem = Scene.Item(ctx, "item.substitute");
            });
            int first = t.D;
            int subHp = t.Hp(sub), firstHp = t.Hp(first);
            Attack(t, "flame_wheel", R, R, R);
            bool subAsked = false;
            Resolve(t, (c, legal) =>
            {
                if (FromCard(t, c, 1)) { subAsked = true; return u == Use.OnlyA ? Decline(legal) : ChoiceAnswer.Of(AnswerKind.Accept); }
                if (FromCard(t, c, 0)) return u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Of(AnswerKind.Accept);
                return c.Sub == ChoiceSub.Fate ? Decline(legal) : null;
            });
            int hitTarget = u == Use.OnlyA ? first : sub;
            int other = hitTarget == first ? sub : first;
            var d = Breakdown(t);
            bool burnedHit = StatusRules.Has(t.S.Pokemon[hitTarget], StatusEffect.Burned);
            bool burnedOther = StatusRules.Has(t.S.Pokemon[other], StatusEffect.Burned);
            int lost = (hitTarget == sub ? subHp : firstHp) - t.Hp(hitTarget);
            o.Note($"쓴 것 {Used(t)} · 대타 물음 {subAsked} · 피해 대상 {(d != null ? (d.TargetId == sub ? "대타" : d.TargetId == first ? "처음" : "?") : "-")} · 화상 대타 {StatusRules.Has(t.S.Pokemon[sub], StatusEffect.Burned)} 처음 {StatusRules.Has(t.S.Pokemon[first], StatusEffect.Burned)} · 잃은 체력 {lost}");
            o.Expect(subAsked, "대타출동 물음(받는 쪽)");
            o.Expect(d != null && d.TargetId == hitTarget, "공격 피해는 나와 있는 포켓몬(대타면 대타)");
            o.Expect(burnedHit == (u != Use.OnlyB) && !burnedOther, "kindler 화상은 지금 나와 있는 포켓몬에게만");
            o.Expect(d != null && lost == d.Final + (u == Use.OnlyB ? 0 : TrainerRules.IgnitionDamage), "kindler 2 피해도 같은 포켓몬");
            return o;
        }

        static Outcome KindlerDelinquent(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "kindler", u == Use.OnlyA ? null : "delinquent", null }, new[] { Monferno }, new[] { Umbreon }, a => a.S.Seats[0].Firewood = 2);
            int before = t.Hp(t.D);
            Attack(t, "flame_wheel", R, R, R);
            Resolve(t, (c, legal) => FromCard(t, c, 0) ? (u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Of(AnswerKind.Accept)) : c.Sub == ChoiceSub.Fate ? Decline(legal) : null);
            var d = Breakdown(t, t.D);
            bool guard = u != Use.OnlyA && HasStep(d, DamageStepKind.Reduction, t.CardOf(1));
            int lost = before - t.Hp(t.D), extra = d != null ? lost - d.Final : -1;
            o.Note($"쓴 것 {Used(t)} · 공격 피해 {d?.Final}(delinquent −2 {guard}) · kindler 몫 {extra}");
            o.Expect(guard == (u != Use.OnlyA), "공격 피해에 delinquent R −2");
            int want = u == Use.OnlyB ? 0 : u == Use.OnlyA ? TrainerRules.IgnitionDamage : Math.Max(0, TrainerRules.IgnitionDamage - 2);
            o.Expect(extra == want, "kindler R 2 피해도 받는 쪽 카드의 감소를 지난다(기술 효과 피해와 같은 꼴)");
            return o;
        }

        static Outcome BlackBeltSkater(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { u == Use.OnlyB ? null : "black_belt", u == Use.OnlyA ? null : "roller_skater", null }, new[] { Lucario }, new[] { Bidoof },
                a => a.S.Pokemon[a.A].Status = StatusEffect.Burned);
            Battles.SetHp(t.S, ctx, t.D, t.Max(t.D));
            Attack(t, "aura_sphere", C, C, C, C);
            Resolve(t, (c, legal) => c.Sub == ChoiceSub.Fate ? Decline(legal) : null);
            var d = Breakdown(t, t.D);
            bool spirit = d != null && Array.Exists(d.Steps, x => x.Kind == DamageStepKind.Reduction && x.Value > 0);
            bool burn = d != null && Array.Exists(d.Steps, x => x.Kind == DamageStepKind.Reduction && x.Source.Kind == SourceKind.Status);
            bool cap = d != null && Array.Exists(d.Steps, x => x.Kind == DamageStepKind.Cap);
            o.Note($"피해 {d?.Final} · 단계 {(d != null ? string.Join(",", Array.ConvertAll(d.Steps, x => $"{x.Kind}{x.Value}")) : "-")}");
            o.Expect(spirit == (u != Use.OnlyB) && burn == (u == Use.OnlyB), "화상 격투: black_belt R이면 −2 대신 +2");
            if (u == Use.OnlyA) o.Expect(d != null && d.Final > 6 && !cap, "상한이 없으면 6보다 크다(장면 조건)");
            else o.Expect(d != null && d.Final <= 6 && (cap || d.Final < 6), "roller_skater R 상한 6이 마지막(⑦)");
            return o;
        }

        // ── 피해 뒤 · 기절 ──

        static Outcome AcolyteRocket(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { u == Use.OnlyB ? null : "undivided_acolyte", u == Use.OnlyA ? null : "team_rocket", null }, new[] { Garchomp }, new[] { Bidoof, Turtwig },
                a => Battles.SetHp(a.S, ctx, a.Theirs[0], 2));
            int hp = t.Hp(t.A);
            Attack(t, "tackle", S, S);
            Resolve(t, (c, legal) => c.Sub == ChoiceSub.Fate ? Decline(legal) : null);
            bool fainted = All<Fainted>(t.Events).Exists(e => e.PokemonId == t.D);
            int lost = hp - t.Hp(t.A);
            o.Note($"쓴 것 {Used(t)} · 받는 쪽 기절 {fainted} · 공격자 잃은 체력 {lost}");
            o.Expect(fainted == (u != Use.OnlyB), "acolyte R로 체력 2 이하 기절");
            o.Expect(lost == (u == Use.Both ? TrainerRules.BlastDamage : 0), "카드로 기절해도 team_rocket R 2(기절한 까닭을 가리지 않는다)");
            if (u == Use.Both) o.Expect(Used(t) == "0:undivided_acolyte.R → 1:team_rocket.R", "공격 뒤(공격 쪽) → 기절(받는 쪽) 순서");
            return o;
        }

        static Outcome AromaRocket(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { u == Use.OnlyB ? null : "aroma_lady", u == Use.OnlyA ? null : "team_rocket", null }, new[] { Torterra }, new[] { Bidoof, Turtwig }, a =>
            {
                Battles.SetHp(a.S, ctx, a.A, 1);
                Battles.SetHp(a.S, ctx, a.Theirs[0], 1);
            });
            Attack(t, "razor_leaf", R, R, R);
            Resolve(t, (c, legal) => c.Sub == ChoiceSub.Fate ? Decline(legal) : null);
            var d = Breakdown(t, t.D);
            int dealt = d != null ? d.Final : 0;
            int heal = u == Use.OnlyB ? 0 : (dealt + 1) / 2;
            int want = Math.Max(0, 1 + heal - (u == Use.OnlyA ? 0 : TrainerRules.BlastDamage));
            bool attackerDown = All<Fainted>(t.Events).Exists(e => e.PokemonId == t.A);
            o.Note($"쓴 것 {Used(t)} · 준 피해 {dealt} · 공격자 체력 {t.Hp(t.A)}(기대 {want}) · 공격자 기절 {attackerDown}");
            o.Expect(t.Hp(t.A) == want, "회복(공격 피해와 같은 버퍼) → 기절 뒤 team_rocket 2");
            o.Expect(attackerDown == (want == 0), "체력 0일 때만 쓰러진다");
            return o;
        }

        static Outcome ScubaWorker(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "scuba_diver", u == Use.OnlyA ? null : "worker", null }, new[] { Piplup, Turtwig }, new[] { Bidoof, Bastiodon }, a =>
            {
                a.S.Pokemon[a.A].Status = StatusEffect.Poisoned;
                Battles.SetHp(a.S, ctx, a.Theirs[0], 1);
            });
            Attack(t, "water_gun", R, R);
            var order = new List<string>();
            Resolve(t, (c, legal) =>
            {
                if (c.Kind == ChoiceKind.SwitchWindow && c.Seat == 0)
                {
                    order.Add(c.Source.Equals(t.CardOf(0)) ? "scuba" : "switch");
                    return u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Pick(t.Mine[1]);
                }
                return c.Sub == ChoiceSub.Fate ? Decline(legal) : null;
            });
            int now = t.S.Battle != null ? t.S.Battle.Sides[0].Active : -1;
            int hit = u == Use.OnlyB ? t.Mine[0] : t.Mine[1];
            var fat = All<FatigueChanged>(t.Events).Find(e => e.Source.Equals(t.CardOf(1)));
            o.Note($"쓴 것 {Used(t)} · 교체 창 {string.Join(",", order)} · 지금 나온 {(now == t.Mine[1] ? "터트윅" : now == t.Mine[0] ? "팽도리" : "-")} · 팽도리 독 {t.S.Pokemon[t.Mine[0]].Status} · worker 피로 대상 {(fat == null ? "-" : fat.PokemonId == t.Mine[1] ? "터트윅" : "팽도리")} · 교체 금지 {now >= 0 && ModRules.NoEscape(t.S.Pokemon[now])}");
            o.Expect(order.Count == 1 && order[0] == "scuba", "기절한 쪽의 상대 교체 창이 scuba_diver 출처");
            o.Expect(now == (u == Use.OnlyB ? t.Mine[0] : t.Mine[1]), "교체하면 터트윅");
            o.Expect((t.S.Pokemon[t.Mine[0]].Status == StatusEffect.None) == (u != Use.OnlyB), "교체해 나간 포켓몬의 독이 풀린다");
            if (u != Use.OnlyA)
            {
                o.Expect(fat != null && fat.PokemonId == hit, "worker R은 그때 나와 있는 공격 쪽 포켓몬에게");
                o.Expect(now >= 0 && ModRules.NoEscape(t.S.Pokemon[now]), "교체 · 도망 금지");
            }
            else o.Expect(fat == null, "worker 없음");
            return o;
        }

        // ── 순서 ──

        static Outcome NinjaGardener(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { "ninja_boy", u == Use.OnlyA ? null : "gardener", null }, new[] { Turtwig }, new[] { Turtwig }, settle: false); // gardener R = 풀 · 바위 +3
            var ask = t.S.Pending.Find(x => !x.Answered && x.Sub == ChoiceSub.InitiativeToken);
            o.Expect(ask != null && ask.Seat == 0, "토큰을 가진 좌석 0에게 묻는다");
            if (ask == null) return o;
            o.Expect(ask.Count == (u == Use.OnlyA ? 0 : 1), "Count 1 = 우선도가 다른데 묻는다(ninja_boy B)");
            t.From = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ask, ChoiceAnswer.Of(u == Use.OnlyB ? AnswerKind.Decline : AnswerKind.Accept), t.Log);
            Resolve(t, null);
            var first = Battles.Only(t.S, ChoiceKind.BattleAction);
            var status = t.S.Pokemon[t.D].Status;
            o.Note($"쓴 것 {Used(t)} · 선턴 좌석 {first.Seat} · 상대 상태 {status} · 토큰 {(t.S.Battle.Sides[1].InitiativeToken ? "좌석 1" : "좌석 0")}");
            o.Expect(first.Seat == (u == Use.OnlyB ? 1 : 0), "토큰을 쓰면 좌석 0 선턴");
            o.Expect((status != StatusEffect.None) == (u != Use.OnlyB), "토큰을 쓰면 Toxin Dart(강제)");
            if (u == Use.Both) o.Expect(Used(t) == "0:ninja_boy.B → 0:ninja_boy.R", "느린 쪽 토큰 선턴(B) → Toxin Dart(R)");
            if (u == Use.OnlyA) o.Expect(Used(t) == "0:ninja_boy.R", "동률 토큰은 보통 규칙 — B는 안 셈");
            if (u != Use.OnlyB) o.Expect(t.S.Battle.Sides[1].InitiativeToken, "쓴 토큰은 상대에게");
            return o;
        }

        // ── 배틀 뒤 ──

        static Outcome RocketBackpacker(Ctx ctx, Use u)
        {
            var o = new Outcome();
            var t = Field(ctx, new[] { u == Use.OnlyA ? null : "backpacker", "team_rocket", null }, new[] { Garchomp }, new[] { Turtwig }, a =>
            {
                a.S.Pokemon[a.A].Damage = 3;
                Battles.SetHp(a.S, ctx, a.Theirs[0], 1);
            });
            Attack(t, "tackle", R, R);
            int far = -1;
            for (int c = ctx.Board.Count - 1; c >= 0 && far < 0; c--)
                if (!ctx.Board[c].DistortionWorld && c != t.S.Seats[1].Cell) far = c;
            Resolve(t, (c, legal) =>
            {
                if (FromCard(t, c, 1)) return u == Use.OnlyB ? Decline(legal) : ChoiceAnswer.Pick(far);
                if (FromCard(t, c, 0)) return ChoiceAnswer.Pick(t.A);
                return null;
            });
            string asked = string.Join(" → ", t.Asked);
            o.Note($"물은 차례 {asked} · 쓴 것 {Used(t)} · 좌석 1 칸 {(t.S.Seats[1].Cell == far ? "먼 칸" : "그 밖")} · 한가브리아스 피해 {t.S.Pokemon[t.A].Damage}");
            int rocket = t.Asked.FindIndex(x => x.StartsWith("1:Reaction/EffectPick"));
            int tent = t.Asked.FindIndex(x => x.StartsWith("0:Reaction/EffectPick"));
            o.Expect(rocket >= 0, "team_rocket R: 쓸 포켓몬이 바닥나 갈 칸을 묻는다");
            o.Expect(All<HpChanged>(t.Events).Exists(e => e.Source.Equals(t.CardOf(1)) && e.PokemonId == t.A), "기절한 순간 team_rocket R 2");
            if (u != Use.OnlyA)
            {
                o.Expect(tent > rocket, "배틀을 닫기 전 물음(갈 칸) → 배틀 뒤 턴 끝 앞 물음(회복)");
                o.Expect(t.S.Pokemon[t.A].Damage == 0, "회복은 2 피해까지 모두");
            }
            else o.Expect(tent < 0, "backpacker 없음");
            o.Expect((t.S.Seats[1].Cell == far) == (u != Use.OnlyB), "갈 칸");
            o.Expect(t.S.Battle == null, "배틀이 닫혔다");
            return o;
        }

        static Outcome BackpackerInterviewers(Ctx ctx, Use u)
        {
            var o = new Outcome();
            // 트레이너전 승리 스탬프는 이긴 좌석이 곧바로 도전 보호(p.9)를 받아 interviewers B가 묻지 못한다(rulings A38 「도전 보호 중인 좌석에는 묻지 않는다」) — 「먼저 쓴 쪽 때문에 뒤가 조건을 잃음」의 규칙 꼴.
            // 배틀 밖 좌석 물음 순서 자체는 TrainerRewardTests.G6_4인판_배틀뒤카드물음순서_회복다음도전_그뒤턴끝(야생 배틀)이 본다. 변형: A만 = 인터뷰어 없음, B만 = 배낭여행자 없음
            var t = Field(ctx, new[] { u == Use.OnlyB ? null : "backpacker", null, u == Use.OnlyA ? null : "interviewers" }, new[] { Garchomp }, new[] { Turtwig }, a =>
            {
                a.S.Pokemon[a.A].Damage = 3;
                Battles.SetHp(a.S, ctx, a.Theirs[0], 1);
                for (int c = 0; c < ctx.Board.Count; c++)
                    if (ctx.Board[c].Wild && !ctx.Board[c].DistortionWorld && !BattleRules.InVillage(ctx, c)) { a.S.Seats[0].Cell = a.S.Seats[1].Cell = c; break; }
            });
            Attack(t, "tackle", R, R);
            bool marked = false;
            Resolve(t, (c, legal) =>
            {
                if (FromCard(t, c, 0)) { marked = t.S.Seats[2].ScoopTarget == 0; return ChoiceAnswer.Pick(t.A); }
                if (FromCard(t, c, 2)) return ChoiceAnswer.Of(AnswerKind.Accept);
                return null;
            });
            var cards = t.Asked.FindAll(x => x.EndsWith(":TrainerCard"));
            o.Note($"카드 물음 차례 {string.Join(" → ", cards)} · 좌석 0 스탬프 {t.S.Seats[0].Stamps[StampRules.TrainerWin]} · 회복 때 인터뷰어 표시 {marked} · 도전 보호 {t.S.Seats[0].Protected} · 남은 표시 {t.S.Seats[2].ScoopTarget} · 새 배틀 {(t.S.Battle != null ? "있음" : "없음")}");
            o.Expect(t.S.Seats[0].Stamps[StampRules.TrainerWin], "트레이너전 승리 스탬프");
            o.Expect(string.Join(" → ", cards) == (u != Use.OnlyB ? "0:Reaction/EffectPick:TrainerCard" : ""), "회복만 묻고 인터뷰어 도전은 묻지 않는다(이긴 좌석은 도전 보호)");
            if (u == Use.Both) o.Expect(marked, "스탬프를 얻은 순간 인터뷰어 표시는 남았다(물을 때 보호로 지운다)");
            o.Expect(t.S.Seats[2].ScoopTarget < 0 && t.S.Battle == null, "표시를 지우고 새 배틀 없음");
            if (u != Use.OnlyB) o.Expect(t.S.Pokemon[t.A].Damage == 0, "회복");
            return o;
        }

        // ── 맵: 남의 행동 ──

        static Outcome GinkgoInterviewers(Ctx ctx, Use u)
        {
            var o = new Outcome();
            GameState s = null;
            for (ulong seed = 1; seed < 60; seed++)
            {
                var g = Flow.Started(ctx, 3, seed);
                if (g.ActiveSeat != 0 && g.Pending.Exists(x => !x.Answered && x.Kind == ChoiceKind.MapAction && x.Seat == g.ActiveSeat)) { s = g; break; }
            }
            Assert.IsNotNull(s, "차례 좌석이 0이 아닌 판");
            var log = new EventLog();
            int actor = s.ActiveSeat, merchant = (actor + 2) % 3, interviewer = (actor + 1) % 3; // merchant의 좌석 번호가 actor보다 작다 — 좌석 번호 순 훑기와 G6 순서(차례 좌석부터)가 갈린다
            if (u != Use.OnlyB) GiveCard(ctx, s, merchant, "ginkgo_merchant");
            GiveCard(ctx, s, interviewer, "interviewers");
            TrainerRules.Refresh(s, ctx);
            var trades = ctx.Decks.Cards[BankRules.Card(s)].Trades;
            var give = trades[BankRules.Top].Give;
            var get = trades[BankRules.Top].Get;
            int missingActor = -1, missingMerchant = (int)give[0];
            for (int c = 0; c < SeatState.ApricornColors && missingActor < 0; c++)
                if (Has(get, (ApricornColor)c) && !Has(give, (ApricornColor)c)) missingActor = c;
            Assert.GreaterOrEqual(missingActor, 0, "받는 규토리에 내는 것과 다른 색이 있다");
            foreach (int seat in new[] { actor, merchant, interviewer })
            {
                for (int c = 0; c < SeatState.ApricornColors; c++) s.Seats[seat].Apricorns[c] = 1;
                for (int i = 0; i < SeatState.StampCount; i++) s.Seats[seat].Stamps[i] = i != StampRules.AllApricorns; // 다른 상태 조건 스탬프(운명 셋 등)가 끼지 않게
            }
            s.Seats[actor].Apricorns[missingActor] = 0;
            foreach (var c in give) s.Seats[actor].Apricorns[(int)c]++;
            s.Seats[merchant].Apricorns[missingMerchant] = u == Use.OnlyA ? 1 : 0; // A만 = merchant는 이미 일곱 색 — 스탬프를 이미 받은 것으로
            if (u == Use.OnlyA) s.Seats[merchant].Stamps[StampRules.AllApricorns] = true;
            s.Seats[interviewer].Apricorns[0] = 0;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Ginkgo, BankRules.Top), log);
            var gained = log.OfType<StampGained>();
            var ask = s.Pending.Find(x => !x.Answered && x.Kind == ChoiceKind.Reaction && x.Seat == interviewer);
            string Who(int seat) => seat == actor ? "거래한 좌석" : seat == merchant ? "ginkgo 좌석" : seat.ToString();
            // 첫 물음에 「안 감」 → 맵 행동을 다시 열기 전에 둘째 물음(줄에 남은 스탬프) → 「안 감」 → 맵 행동
            var asks = new List<string>();
            for (int guard = 0; guard < 4 && ask != null; guard++)
            {
                asks.Add(Who(ask.Subject));
                Flow.Answer(s, ctx, ChoiceKind.Reaction, ask.Default.Clone(), log); // 「may」 — 첫 답 = 안 감
                ask = s.Pending.Find(x => !x.Answered && x.Kind == ChoiceKind.Reaction && x.Seat == interviewer);
            }
            bool mapBack = s.Pending.Exists(x => !x.Answered && x.Kind == ChoiceKind.MapAction && x.Seat == actor);
            o.Note($"스탬프 {string.Join(", ", gained.ConvertAll(e => Who(e.Seat)))} · 인터뷰어 물음 차례 {string.Join(" → ", asks)} · 맵 행동으로 돌아옴 {mapBack}");
            o.Expect(gained.Exists(e => e.Seat == actor), "거래한 좌석이 일곱 색 스탬프");
            o.Expect(gained.Exists(e => e.Seat == merchant) == (u == Use.Both), "ginkgo 좌석도 받은 규토리로 일곱 색 스탬프");
            if (u == Use.Both) o.Expect(gained.Count >= 2 && gained[0].Seat == actor, "스탬프는 차례 좌석부터 받는다(G6 맵 창 순서)");
            o.Expect(string.Join(" → ", asks) == (u == Use.Both ? "거래한 좌석 → ginkgo 좌석" : "거래한 좌석"), "인터뷰어는 차례 좌석부터(G6 맵 창 순서) 얻은 스탬프마다 묻는다");
            o.Expect(mapBack, "다 물은 뒤 차례 좌석의 맵 행동");
            return o;
        }

        static Outcome CollectorInterviewers(Ctx ctx, Use u)
        {
            // 거래한 차례 좌석(collector)이 일곱 색 스탬프(7번째 — 보상 = 수호신의 서) → collector B 보상 두 번 · interviewers B는 그 좌석에 도전할지 묻는다. 변형: A만 = 인터뷰어 없음, B만 = 수집가 없음
            var o = new Outcome();
            GameState s = null;
            for (ulong seed = 1; seed < 60 && s == null; seed++)
            {
                var g = Flow.Started(ctx, 3, seed);
                if (g.Pending.Exists(x => !x.Answered && x.Kind == ChoiceKind.MapAction && x.Seat == g.ActiveSeat)) s = g;
            }
            Assert.IsNotNull(s);
            var log = new EventLog();
            int actor = s.ActiveSeat, interviewer = (actor + 1) % 3;
            if (u != Use.OnlyB) GiveCard(ctx, s, actor, "collector");
            if (u != Use.OnlyA) GiveCard(ctx, s, interviewer, "interviewers");
            TrainerRules.Refresh(s, ctx);
            var trade = ctx.Decks.Cards[BankRules.Card(s)].Trades[BankRules.Top];
            int missing = -1;
            for (int c = 0; c < SeatState.ApricornColors && missing < 0; c++)
                if (Has(trade.Get, (ApricornColor)c) && !Has(trade.Give, (ApricornColor)c)) missing = c;
            Assert.GreaterOrEqual(missing, 0);
            foreach (int seat in new[] { 0, 1, 2 })
            {
                for (int c = 0; c < SeatState.ApricornColors; c++) s.Seats[seat].Apricorns[c] = 1;
                for (int i = 0; i < SeatState.StampCount; i++) s.Seats[seat].Stamps[i] = i != StampRules.AllApricorns;
            }
            s.Seats[actor].Apricorns[missing] = 0;
            foreach (var c in trade.Give) s.Seats[actor].Apricorns[(int)c]++;
            s.Seats[interviewer].Apricorns[0] = 0;
            int bag = s.Seats[actor].Bag.Count;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Ginkgo, BankRules.Top), log);
            int got = s.Seats[actor].Bag.Count - bag;
            var ask = s.Pending.Find(x => !x.Answered && x.Kind == ChoiceKind.Reaction && x.Seat == interviewer);
            bool twice = log.OfType<AbilityUsed>().Exists(e => e.Seat == actor);
            o.Note($"스탬프 {log.OfType<StampGained>().Count} · 가방에 든 것 {got} · collector B {twice} · 인터뷰어 물음 {(ask != null ? (ask.Subject == actor ? "거래한 좌석" : ask.Subject.ToString()) : "없음")}");
            o.Expect(log.OfType<StampGained>().Exists(e => e.Seat == actor), "거래한 좌석이 일곱 색 스탬프");
            o.Expect(got == (u != Use.OnlyB ? 2 : 1), "7번째 보상(수호신의 서) — collector B면 두 번");
            o.Expect((ask != null && ask.Subject == actor) == (u != Use.OnlyA), "인터뷰어는 보상을 두 번 받은 뒤에도 그 스탬프로 묻는다");
            return o;
        }
    }
}
