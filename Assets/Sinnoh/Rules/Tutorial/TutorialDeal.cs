using System;
using System.Collections.Generic;
using Sinnoh.Rules.Session;

namespace Sinnoh.Rules.Tutorial
{
    /// <summary>
    /// 정해진 판(07 §6-1) + 주사위 대본 ForcedDice(07 §6-2). 세션 설정의 <see cref="SessionConfig.Rig"/>로 건다 — 상태 칸이 아니라 세이브·복제·해시에 실리지 않는다.
    /// 원칙(isekai ScriptedDeal과 같다): 섞기·굴림은 평소대로 난수를 쓰고(소비량이 같다) 결과의 순서·면만 고친다. 지정하지 않은 것은 시드가 정한다.
    /// <list type="bullet">
    /// <item>덱 윗장 <see cref="Top"/>: 그 덱에 남아 있는 지정 카드를 맨 위에 지정 순서로(첫째가 맨 위) 둔다 — 붙을 때(NewGame이 섞은 뒤) 한 번, 그 덱을 섞을 때마다(조우 덱은 뽑을 때 섞는다, R1).
    /// 덱을 떠난 카드는 빠진다. 나머지 카드의 앞뒤 순서는 그대로다.</item>
    /// <item>트레이너 카드 <see cref="TrainerCards"/>: 셋업이 좌석 0부터 덱 맨 위 3장씩 나눠 주는 순서(SetupFlow.Begin)로 트레이너 카드 덱 윗장을 정한다.</item>
    /// <item>숨겨진 장소 <see cref="HiddenSpot"/>: 놓이는 칸은 시드가 정하고(R3), 놓인 뒤 그 칸의 뒷면 토큰과 지정 정체의 뒷면 토큰을 맞바꾼다(정체·금색 ? 여부).
    /// 지역 겹침 피하기(R3)는 정체를 가리지 않아 칸이 그대로다 — 다만 유물을 보통 숨겨진 장소 자리로 옮기면 「유물 먼저」 순서의 지역 기록과 다를 수 있다(튜토리얼 판에서만).</item>
    /// <item>주사위 <see cref="Force"/>: 다음에 굴리는 공격·포획 주사위 면 줄. 굴린 개수보다 적게 남았으면 각본 오류(던진다). 비어 있으면 평소 굴림.</item>
    /// </list>
    /// 키가 없거나 같은 덱에 한 카드를 장수보다 많이 쓰면 ArgumentException(만들 때), 지정 카드가 그 덱에 없으면 InvalidOperationException(손볼 때 — 각본이 판과 어긋났다).
    /// </summary>
    public sealed class TutorialDeal : ISessionRig
    {
        readonly Ctx ctx;
        readonly List<int>[] tops = new List<int>[GameState.DeckCount];
        readonly List<KeyValuePair<int, int>> spots = new List<KeyValuePair<int, int>>();
        readonly Queue<AttackDieFace> dice = new Queue<AttackDieFace>();
        GameState host;
        bool spotsDone;

        public TutorialDeal(Ctx ctx)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>남은 주사위 대본(장·단계 끝에 0이어야 한다).</summary>
        public int DiceLeft => dice.Count;

        /// <summary>대본대로 바꾼 주사위 수(누적).</summary>
        public int DiceForced { get; private set; }

        /// <summary>덱 윗장 고정(07 §6-1). keys = 그 덱이 담는 표의 키(포켓몬 · 카드 · 트레이너 카드), 첫째가 맨 위. 같은 덱에 다시 부르면 바꾼다.</summary>
        public TutorialDeal Top(DeckId deck, params string[] keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            var cards = new List<int>(keys.Length);
            foreach (var k in keys) cards.Add(CardOf(deck, k));
            CheckCopies(deck, cards);
            tops[(int)deck] = cards;
            return this;
        }

        /// <summary>트레이너 카드 3장씩(A20-1): perSeat[좌석] = 그 좌석이 받을 3장의 키. SetupFlow.Begin이 좌석 0부터 덱 맨 위를 3장씩 나눈다.</summary>
        public TutorialDeal TrainerCards(params string[][] perSeat)
        {
            if (perSeat == null) throw new ArgumentNullException(nameof(perSeat));
            var keys = new List<string>();
            for (int i = 0; i < perSeat.Length; i++)
            {
                if (perSeat[i] == null || perSeat[i].Length != SetupFlow.TrainerCardOffer)
                    throw new ArgumentException($"좌석 {i}: 트레이너 카드는 {SetupFlow.TrainerCardOffer}장", nameof(perSeat));
                keys.AddRange(perSeat[i]);
            }
            return Top(DeckId.TrainerCard, keys.ToArray());
        }

        /// <summary>뒷면 숨겨진 장소 정체 고정: 셋업이 그 칸에 숨겨진 장소를 놓으면 정체를 itemKey(items.json 키 — 숨겨진 장소·우두머리·유물)로 맞춘다.</summary>
        public TutorialDeal HiddenSpot(int cell, string itemKey)
        {
            if (cell < 0 || cell >= ctx.Board.Count) throw new ArgumentOutOfRangeException(nameof(cell), cell, "보드 칸 밖");
            if (!ctx.Items.Ids.TryIndexOf(itemKey, out int def)) throw new ArgumentException($"없는 아이템 키 {itemKey}", nameof(itemKey));
            var k = ctx.Items.Items[def].Kind;
            if (k != ItemKind.HiddenSpot && k != ItemKind.AlphaPokemon && k != ItemKind.Artifact && k != ItemKind.CelestialArtifact)
                throw new ArgumentException($"{itemKey}: 숨겨진 장소 토큰이 아니다({k})", nameof(itemKey));
            foreach (var p in spots)
                if (p.Key == cell) throw new ArgumentException($"칸 {cell}을 두 번 정했다", nameof(cell));
            int same = 0;
            foreach (var p in spots)
                if (p.Value == def) same++;
            if (same >= (ctx.Items.Items[def].Copies ?? 0)) throw new ArgumentException($"{itemKey}: 토큰 {ctx.Items.Items[def].Copies}개보다 많이 정했다", nameof(itemKey));
            spots.Add(new KeyValuePair<int, int>(cell, def));
            return this;
        }

        /// <summary>주사위 대본을 뒤에 더한다(07 §6-2 — 각본 단계의 dice).</summary>
        public TutorialDeal Force(params AttackDieFace[] faces)
        {
            if (faces == null) throw new ArgumentNullException(nameof(faces));
            foreach (var f in faces)
            {
                if (f == AttackDieFace.None) throw new ArgumentException("굴림에는 None 면이 없다", nameof(faces));
                dice.Enqueue(f);
            }
            return this;
        }

        /// <summary>남은 주사위 대본을 버린다.</summary>
        public void ClearDice() => dice.Clear();

        // ── ISessionRig ──

        public void Attach(GameState s)
        {
            host = s ?? throw new ArgumentNullException(nameof(s));
            spotsDone = false;
            for (int d = 0; d < tops.Length; d++)
                if (tops[d] != null) Arrange(d, true);
        }

        /// <summary>
        /// 막 앞 판 손보기(07 T25)로 세션을 새 상태에서 다시 열 때: 그 상태를 붙잡는다(덱 윗장은 다음에 섞을 때부터 · 이미 놓인 숨겨진 장소는 다시 맞바꾸지 않는다).
        /// 새 판의 Attach와 달리 지정 카드가 덱에 남아 있지 않아도 된다(셋업이 이미 나눠 줬다).
        /// </summary>
        public void Rebase(GameState s)
        {
            host = s ?? throw new ArgumentNullException(nameof(s));
        }

        public void Settle(GameState s)
        {
            if (spotsDone || spots.Count == 0 || !ReferenceEquals(s, host)) return;
            bool placed = false;
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.Cell >= 0) { placed = true; break; }
            if (!placed) return;
            spotsDone = true;
            foreach (var p in spots)
            {
                var at = TokenAt(s, p.Key) ?? throw new InvalidOperationException($"칸 {p.Key}에 뒷면 숨겨진 장소가 놓이지 않았다(시드가 정한 칸 — 각본의 칸을 고친다)");
                if (at.Def == p.Value) continue;
                TokenState from = null;
                foreach (var t in s.Tokens) // 보드 밖 토큰을 먼저 — 다른 정해진 칸을 흔들지 않게
                    if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Def == p.Value && !Fixed(t) && (from == null || from.Cell >= 0 && t.Cell < 0)) from = t;
                if (from == null) throw new InvalidOperationException($"{ctx.Items.Items[p.Value].Key}: 맞바꿀 뒷면 토큰이 없다");
                int def = at.Def;
                bool special = at.Special;
                at.Def = from.Def;
                at.Special = from.Special;
                from.Def = def;
                from.Special = special;
            }
        }

        bool Fixed(TokenState t)
        {
            foreach (var p in spots)
                if (t.Cell == p.Key && t.Def == p.Value) return true;
            return false;
        }

        static TokenState TokenAt(GameState s, int cell)
        {
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.Cell == cell && !t.FaceUp && !t.Destroyed) return t;
            return null;
        }

        public void Shuffled(List<int> pile)
        {
            if (host == null) return;
            for (int d = 0; d < tops.Length; d++)
                if (tops[d] != null && ReferenceEquals(pile, host.Decks[d]))
                {
                    Arrange(d, false);
                    return;
                }
        }

        public void Rolled(AttackDieFace[] faces)
        {
            if (dice.Count == 0) return;
            if (dice.Count < faces.Length) throw new InvalidOperationException($"주사위 대본이 모자라다: {faces.Length}개를 굴렸는데 {dice.Count}개 남음");
            for (int i = 0; i < faces.Length; i++) faces[i] = dice.Dequeue();
            DiceForced += faces.Length;
        }

        /// <summary>덱 d에 남은 지정 카드를 맨 위(끝)에 지정 순서로. 덱을 떠난 카드는 건너뛴다 — 다만 붙을 때(strict)는 모두 있어야 한다.</summary>
        void Arrange(int d, bool strict)
        {
            var pile = host.Decks[d];
            var want = tops[d];
            var moved = new List<int>(want.Count);
            foreach (int card in want)
            {
                int at = pile.LastIndexOf(card);
                if (at < 0)
                {
                    if (strict) throw new InvalidOperationException($"{(DeckId)d}: 지정한 카드 {card}가 덱에 없다");
                    continue;
                }
                pile.RemoveAt(at);
                moved.Add(card);
            }
            for (int i = moved.Count - 1; i >= 0; i--) pile.Add(moved[i]); // 끝 = 맨 위 = 첫째
        }

        int CardOf(DeckId deck, string key)
        {
            var table = Ctx.KindOf(deck) == DeckKind.Pokemon ? ctx.Cards.PokemonIds : Ctx.KindOf(deck) == DeckKind.TrainerCard ? ctx.Cards.TrainerCardIds : ctx.Decks.CardIds;
            if (!table.TryIndexOf(key, out int id)) throw new ArgumentException($"{deck}: 없는 키 {key}", nameof(key));
            bool inDeck = false;
            foreach (var e in ctx.Deck(deck).Cards)
                if (e.Card == id) { inDeck = true; break; }
            if (!inDeck) throw new ArgumentException($"{deck}: {key}는 이 덱의 카드가 아니다", nameof(key));
            return id;
        }

        void CheckCopies(DeckId deck, List<int> cards)
        {
            foreach (int card in cards)
            {
                int used = 0, copies = 0;
                foreach (int c in cards)
                    if (c == card) used++;
                foreach (var e in ctx.Deck(deck).Cards)
                    if (e.Card == card) copies += e.Copies;
                if (used > copies) throw new ArgumentException($"{deck}: 카드 {card}를 {used}번 썼다(장수 {copies})", nameof(cards));
            }
        }
    }
}
