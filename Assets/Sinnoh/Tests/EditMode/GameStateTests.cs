using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-b: GameState(01 §2-2)·손으로 쓴 CopyFrom·StateHash(FNV-1a 64)·Engine.NewGame.
    /// 복제 반사 시험(01 §12 「복제·해시」): GameState에서 닿는 모든 필드를 반사로 훑어 ① 복사되고 ② 원본과 객체를 나누지 않고 ③ 해시에 들어가는지 본다.
    /// 필드를 더하고 CopyFrom·Walk에 빠뜨리면 여기서 실패한다.
    /// </summary>
    public class GameStateTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        GameState New(int seats, ulong seed, Campaign campaign = Campaign.HeartOfSinnoh, RuleOptions opt = null) =>
            Engine.NewGame(ctx, opt ?? new RuleOptions(), seats, seed, campaign);

        [Test]
        public void M3_FNV1a64참조값()
        {
            // FNV 참조 시험값(isthe.com/chongo/tech/comp/fnv): "" · "a" · "foobar"
            Assert.AreEqual(0xcbf29ce484222325UL, new Fnv1a64().Value);
            var a = new Fnv1a64();
            a.Byte((byte)'a');
            Assert.AreEqual(0xaf63dc4c8601ec8cUL, a.Value);
            var f = new Fnv1a64();
            foreach (char c in "foobar") f.Byte((byte)c);
            Assert.AreEqual(0x85944171f73967e8UL, f.Value);
            // int는 리틀 엔디언 4바이트
            var i = new Fnv1a64();
            i.Int(0x61);
            var b = new Fnv1a64();
            foreach (byte x in new byte[] { 0x61, 0, 0, 0 }) b.Byte(x);
            Assert.AreEqual(b.Value, i.Value);
        }

        [Test]
        public void M3_같은시드같은해시()
        {
            for (int n = 1; n <= SeatId.MaxSeats; n++)
            {
                var a = New(n, 20261107);
                var b = New(n, 20261107);
                ulong h = StateHash.Of(a);
                Assert.AreEqual(h, StateHash.Of(b), $"{n}인 같은 시드");
                Assert.AreEqual(h, StateHash.Of(a.Clone()), $"{n}인 복제");
                Assert.AreNotEqual(h, StateHash.Of(New(n, 20261108)), $"{n}인 다른 시드");
                if (n < SeatId.MaxSeats) Assert.AreNotEqual(h, StateHash.Of(New(n + 1, 20261107)), $"{n}인 ≠ {n + 1}인");
                Assert.AreNotEqual(h, StateHash.Of(New(n, 20261107, Campaign.UltraBurst)), $"{n}인 캠페인");
            }

            var opt = new RuleOptions { InitiativeTie = InitiativeTie.BasePriority };
            Assert.AreNotEqual(StateHash.Of(New(2, 1)), StateHash.Of(New(2, 1, opt: opt)), "옵션이 다르면");

            // 복제본을 같은 난수로 섞으면 같은 해시, 원본은 그대로
            var s = New(3, 99);
            ulong before = StateHash.Of(s);
            var c1 = s.Clone();
            var c2 = s.Clone();
            Piles.Shuffle(c1.Decks[(int)DeckId.Weak], c1.Rng);
            Piles.Shuffle(c2.Decks[(int)DeckId.Weak], c2.Rng);
            Assert.AreEqual(StateHash.Of(c1), StateHash.Of(c2));
            Assert.AreNotEqual(before, StateHash.Of(c1));
            Assert.AreEqual(before, StateHash.Of(s), "원본이 바뀌었다");
        }

        [Test]
        public void M3_새게임초기상태()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => New(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => New(SeatId.MaxSeats + 1, 1));

            var s = New(4, 7);
            Assert.AreEqual(4, s.SeatCount);
            Assert.AreEqual(Phase.Setup, s.Phase);
            Assert.IsNotNull(s.Hos);
            Assert.IsNull(s.Ub);
            Assert.IsNull(s.Battle);
            Assert.AreEqual(-1, s.Hos.Act);
            Assert.AreEqual(0, s.Pending.Count);
            Assert.AreEqual(SeatId.HostViewer, s.Viewer);
            foreach (var seat in s.Seats)
            {
                Assert.AreEqual(3, seat.OpenSlots, "A3 신오의 심장 3칸");
                CollectionAssert.AreEqual(new[] { -1, -1, -1, -1, -1, -1 }, seat.Party);
                Assert.AreEqual(-1, seat.HomeVillage);
                Assert.AreEqual(-1, seat.Cell);
            }

            // 덱: 장수 보존, 막은 서막이 맨 위
            for (int i = 0; i < GameState.DeckCount; i++)
            {
                var id = (DeckId)i;
                Assert.AreEqual(ctx.Deck(id).Count, s.Decks[i].Count, id.ToString());
                Assert.AreEqual(0, s.Discards[i].Count, id.ToString());
            }
            var act = s.Decks[(int)DeckId.Act];
            Assert.AreEqual(ctx.Decks.CardIds.IndexOf("act.incursion"), act[act.Count - 1]);
            Assert.AreEqual(ctx.Decks.CardIds.IndexOf("act.choice"), act[0]);
            Assert.AreEqual(42, s.Decks[(int)DeckId.ChangeOfFate].Count);
            Assert.AreEqual(51, s.Decks[(int)DeckId.TrainerCard].Count);

            // 토큰: 나무·산호 118, 왜곡된 나무 6, 사당 13, 전설 사당 7, 숨겨진 장소·유물 52(보드 밖 뒷면)
            int Count(TokenKind k)
            {
                int n = 0;
                foreach (var t in s.Tokens)
                    if (t.Kind == k) n++;
                return n;
            }
            Assert.AreEqual(118, Count(TokenKind.ApricornTree) + Count(TokenKind.ApricornReef));
            Assert.AreEqual(6, Count(TokenKind.DistortedTree));
            Assert.AreEqual(13, Count(TokenKind.Shrine));
            Assert.AreEqual(7, Count(TokenKind.LegendaryShrine));
            Assert.AreEqual(52, Count(TokenKind.HiddenSpot));
            foreach (var t in s.Tokens)
            {
                if (t.Kind == TokenKind.HiddenSpot)
                {
                    Assert.AreEqual(-1, t.Cell);
                    Assert.IsFalse(t.FaceUp);
                }
                else
                {
                    Assert.AreEqual(ctx.Fixtures.Fixtures[t.Def].Cell, t.Cell);
                    Assert.IsTrue(t.FaceUp);
                }
            }

            var ub = New(2, 7, Campaign.UltraBurst, new RuleOptions { UbDifficulty = UbDifficulty.VeteranTrainer });
            Assert.IsNull(ub.Hos);
            Assert.AreEqual(1, ub.Ub.Blessings, "A14 베테랑 1");
            Assert.AreEqual(UbState.PrismArmourTotal, ub.Ub.PrismUltraBurst);
            Assert.AreEqual(6, ub.Seats[0].OpenSlots, "A3 울트라 버스트 6칸");
        }

        [Test]
        public void M3_복제는모든필드를복사()
        {
            var orig = New(2, 5);
            int counter = 0;
            Fill(orig, RootNode(), grow: false, ref counter);
            ulong h = StateHash.Of(orig);

            var nodes = new Walker();
            nodes.Collect(orig, RootNode());
            Assert.Greater(nodes.Leaves.Count, 1000, "훑은 값이 너무 적다");

            // ① 새 상태와 ② 값이 다른 큰 상태에 복사해 본다(목록 원소 재사용·줄이기)
            var fresh = new GameState();
            fresh.CopyFrom(orig);
            var dirty = New(6, 77, Campaign.UltraBurst);
            Fill(dirty, RootNode(), grow: true, ref counter);
            dirty.CopyFrom(orig);
            foreach (var clone in new[] { fresh, dirty })
            {
                foreach (var n in nodes.Counts)
                    Assert.AreEqual(((ICollection)n.Get(orig)).Count, ((ICollection)n.Get(clone)).Count, $"{n.Path} 길이가 복사되지 않았다");
                foreach (var n in nodes.Leaves)
                    Assert.AreEqual(n.Get(orig), n.Get(clone), $"{n.Path}가 복사되지 않았다(CopyFrom)");
                foreach (var n in nodes.References)
                    Assert.IsFalse(ReferenceEquals(n.Get(orig), n.Get(clone)), $"{n.Path}를 원본과 같이 쓴다(얕은 복사)");
                Assert.AreEqual(h, StateHash.Of(clone), "복제 해시");
            }

            // ③ 값 하나씩 바꾸면 해시가 바뀌고 원본은 그대로(Viewer만 해시 밖)
            var c = orig.Clone();
            foreach (var n in nodes.Leaves)
            {
                object was = n.Get(c);
                object origWas = n.Get(orig);
                n.Set(c, Bump(was, 1));
                if (NotHashed.Contains(n.Path)) Assert.AreEqual(h, StateHash.Of(c), $"{n.Path}는 해시에 넣지 않는다(01 §2-3)");
                else Assert.AreNotEqual(h, StateHash.Of(c), $"{n.Path}가 해시에 없다(Walk)");
                Assert.AreEqual(origWas, n.Get(orig), $"{n.Path}: 복제본을 바꿨는데 원본이 바뀌었다");
                n.Set(c, was);
            }
            foreach (var n in nodes.Lists)
            {
                var list = (IList)n.Get(c);
                int origCount = ((IList)n.Get(orig)).Count;
                list.Add(NewElement(n.Type.GetGenericArguments()[0]));
                Assert.AreNotEqual(h, StateHash.Of(c), $"{n.Path} 길이가 해시에 없다");
                Assert.AreEqual(origCount, ((IList)n.Get(orig)).Count, $"{n.Path}: 원본 목록이 늘었다");
                list.RemoveAt(list.Count - 1);
            }
            foreach (string path in Nullable)
            {
                var f = typeof(GameState).GetField(path);
                object was = f.GetValue(c);
                f.SetValue(c, null);
                Assert.AreNotEqual(h, StateHash.Of(c), $"{path} 있음/없음이 해시에 없다");
                var target = orig.Clone();
                target.CopyFrom(c);
                Assert.IsNull(f.GetValue(target), $"{path}: null이 복사되지 않았다");
                f.SetValue(c, was);
            }
            Assert.AreEqual(h, StateHash.Of(c), "되돌린 뒤 해시");
        }

        [Test]
        public void M7_되읽기는모든필드()
        {
            // 02 §4-4: 손님은 받은 스트림(StateStream — Walk 순서)을 GameState로 되읽는다. 모든 값을 기본값과 다르게 채운 상태 → 스트림 → 되읽기가 원본과 같은지 반사로 훑는다.
            // Read에 필드를 빠뜨리거나 순서를 틀리면 값이 어긋나거나 구역 경계에서 FormatException이 난다.
            var orig = New(2, 5);
            int counter = 0;
            Fill(orig, RootNode(), grow: false, ref counter);
            var nodes = new Walker();
            nodes.Collect(orig, RootNode());
            var stream = StateStream.Of(orig);
            Assert.AreEqual(StateHash.Of(orig), StateStream.Hash(stream), "스트림 해시 = StateHash");

            var dirty = New(6, 77, Campaign.UltraBurst);
            Fill(dirty, RootNode(), grow: true, ref counter);
            foreach (var (name, back) in new[] { ("새 상태", StateStream.Read(stream, orig.Viewer)), ("큰 상태에 덮어쓰기", StateStream.Read(stream, orig.Viewer, dirty)) })
            {
                foreach (var n in nodes.Counts)
                    Assert.AreEqual(((ICollection)n.Get(orig)).Count, ((ICollection)n.Get(back)).Count, $"{name}: {n.Path} 길이가 되읽히지 않았다");
                foreach (var n in nodes.Leaves)
                {
                    if (NotHashed.Contains(n.Path) && n.Path != "Viewer") continue; // 해시 밖 값은 스트림에도 없다(부르는 쪽이 정한다)
                    Assert.AreEqual(n.Get(orig), n.Get(back), $"{name}: {n.Path}가 되읽히지 않았다(Read)");
                }
                Assert.AreEqual(StateHash.Of(orig), StateHash.Of(back), $"{name}: 되읽은 해시");
            }

            // null 객체(Hos·Ub·Battle)도 되읽는다
            var c = orig.Clone();
            foreach (string path in Nullable) typeof(GameState).GetField(path).SetValue(c, null);
            var filled = orig.Clone();
            StateStream.Read(StateStream.Of(c), c.Viewer, filled);
            foreach (string path in Nullable) Assert.IsNull(typeof(GameState).GetField(path).GetValue(filled), $"{path}: null이 되읽히지 않았다");
            Assert.AreEqual(StateHash.Of(c), StateHash.Of(filled));

            // 모양이 틀린 스트림은 FormatException(손님은 Resync)
            var cut = (int[][])stream.Clone();
            cut[1] = new int[cut[1].Length - 1];
            Array.Copy(stream[1], cut[1], cut[1].Length);
            Assert.Throws<FormatException>(() => StateStream.Read(cut, 0), "구역 하나가 짧다");
            var extra = (int[][])stream.Clone();
            extra[extra.Length - 1] = new int[stream[stream.Length - 1].Length + 1];
            Assert.Throws<FormatException>(() => StateStream.Read(extra, 0), "끝에 값이 남는다");
            var fewer = new int[stream.Length - 1][];
            Array.Copy(stream, fewer, fewer.Length);
            Assert.Throws<FormatException>(() => StateStream.Read(fewer, 0), "구역이 모자란다");
        }

        /// <summary>해시에 넣지 않는 필드(01 §2-3: Viewer와 캐시).</summary>
        // Options.FixedEffectOrder: 저장 머리의 규칙 판에서 세션이 정하는 값(결정 72 전 판) — 저장·해시에 넣지 않는다
        static readonly HashSet<string> NotHashed = new HashSet<string> { "Viewer", "Options.FixedEffectOrder" };

        /// <summary>null일 수 있는 GameState 필드. 나머지 객체 필드는 늘 있다.</summary>
        static readonly string[] Nullable = { "Hos", "Ub", "Battle" };

        // ── 반사 훑기 ──

        sealed class Node
        {
            public string Path;
            public Type Type;
            public Func<object, object> Get;
            public Action<object, object> Set;
        }

        static Node RootNode() => new Node { Path = "", Type = typeof(GameState), Get = r => r, Set = (r, v) => throw new InvalidOperationException("뿌리") };

        static bool IsLeaf(Type t) => t.IsEnum || t == typeof(int) || t == typeof(bool) || t == typeof(ulong) || t == typeof(long) || t == typeof(uint);

        static bool IsList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);

        static bool IsComposite(Type t) => t.Namespace == typeof(GameState).Namespace && (t.IsClass || (t.IsValueType && !t.IsEnum));

        static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;

        static IEnumerable<Node> Children(object root, Node node)
        {
            var t = node.Type;
            if (t.IsArray)
            {
                var et = t.GetElementType();
                int len = ((Array)node.Get(root)).Length;
                for (int i = 0; i < len; i++)
                {
                    int k = i;
                    yield return new Node
                    {
                        Path = $"{node.Path}[{k}]", Type = et,
                        Get = r => ((Array)node.Get(r)).GetValue(k),
                        Set = (r, v) => ((Array)node.Get(r)).SetValue(v, k),
                    };
                }
            }
            else if (IsList(t))
            {
                var et = t.GetGenericArguments()[0];
                int len = ((IList)node.Get(root)).Count;
                for (int i = 0; i < len; i++)
                {
                    int k = i;
                    yield return new Node
                    {
                        Path = $"{node.Path}[{k}]", Type = et,
                        Get = r => ((IList)node.Get(r))[k],
                        Set = (r, v) => ((IList)node.Get(r))[k] = v,
                    };
                }
            }
            else
            {
                Assert.IsTrue(IsComposite(t), $"{node.Path}: 허용하지 않는 형 {t} — 상태에는 int·bool·ulong·enum·배열·List·Sinnoh.Rules 형만");
                var hidden = t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.AreEqual(0, hidden.Length, $"{t.Name}: 숨은 필드 {(hidden.Length > 0 ? hidden[0].Name : "")} — 상태 필드는 public으로(자동 속성 금지)");
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var field = f;
                    yield return new Node
                    {
                        Path = Join(node.Path, field.Name), Type = field.FieldType,
                        Get = r => field.GetValue(node.Get(r)),
                        Set = (r, v) =>
                        {
                            object parent = node.Get(r);
                            field.SetValue(parent, v);
                            if (parent.GetType().IsValueType) node.Set(r, parent); // 구조체는 상자를 다시 써 넣는다
                        },
                    };
                }
            }
        }

        sealed class Walker
        {
            public readonly List<Node> Leaves = new List<Node>();
            public readonly List<Node> References = new List<Node>();
            public readonly List<Node> Lists = new List<Node>();
            public readonly List<Node> Counts = new List<Node>();

            public void Collect(object root, Node node)
            {
                if (IsLeaf(node.Type))
                {
                    Leaves.Add(node);
                    return;
                }
                object v = node.Get(root);
                Assert.IsNotNull(v, $"{node.Path}가 null이다(Fill 뒤)");
                if (!node.Type.IsValueType) References.Add(node);
                if (node.Type.IsArray || IsList(node.Type)) Counts.Add(node);
                if (IsList(node.Type)) Lists.Add(node);
                foreach (var c in Children(root, node)) Collect(root, c);
            }
        }

        /// <summary>모든 값을 기본값과 다르게 바꾸고, 빈 목록·null 객체를 채운다. grow면 목록마다 원소를 하나 더한다.</summary>
        static void Fill(object root, Node node, bool grow, ref int counter)
        {
            if (IsLeaf(node.Type))
            {
                node.Set(root, Bump(node.Get(root), ++counter));
                return;
            }
            object v = node.Get(root);
            if (v == null)
            {
                Assert.IsFalse(node.Type.IsArray, $"{node.Path}: 배열은 생성자에서 만든다");
                v = NewElement(node.Type);
                node.Set(root, v);
            }
            if (IsList(node.Type))
            {
                var list = (IList)v;
                int add = list.Count == 0 ? 2 : grow ? 1 : 0;
                for (int i = 0; i < add; i++) list.Add(NewElement(node.Type.GetGenericArguments()[0]));
            }
            foreach (var c in Children(root, node)) Fill(root, c, grow, ref counter);
        }

        static object NewElement(Type t)
        {
            var ctor = t.IsValueType ? null : t.GetConstructor(Type.EmptyTypes);
            Assert.IsTrue(t.IsValueType || ctor != null, $"{t.Name}: 매개변수 없는 생성자가 있어야 한다");
            return Activator.CreateInstance(t);
        }

        static object Bump(object v, int delta)
        {
            switch (v)
            {
                case bool b: return !b;
                case int i: return unchecked(i + delta);
                case uint u: return unchecked(u + (uint)delta);
                case long l: return unchecked(l + delta);
                case ulong ul: return unchecked(ul + (ulong)delta);
            }
            var t = v.GetType();
            Assert.IsTrue(t.IsEnum, $"값 형 {t}");
            return Enum.ToObject(t, Convert.ToInt64(v) + delta);
        }
    }
}
