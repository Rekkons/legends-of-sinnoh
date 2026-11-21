using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>덱이 담는 카드의 표: Pokemon = pokemon, TrainerCard = trainer_cards, Card = cards(이미지 전용 카드).</summary>
    public enum DeckKind { Pokemon, TrainerCard, Card }

    /// <summary>덱 안 카드 한 종류와 장수. Card = 덱 종류가 정한 표의 색인.</summary>
    public sealed class DeckEntry
    {
        public readonly int Card;
        public readonly int Copies;

        public DeckEntry(int card, int copies)
        {
            if (card < 0) throw new ArgumentOutOfRangeException(nameof(card), card, "카드 색인은 0 이상");
            if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies), copies, "장수는 1 이상");
            Card = card;
            Copies = copies;
        }
    }

    /// <summary>
    /// 덱 하나(decks.json 행, 세이브 덱 17개). Cards = 바탕 카드, AltStates = 그 카드들의 다른 면(TTS 상태).
    /// 포켓몬 덱의 다른 면은 그 면의 pokemon 색인(예: 로토무 479:1의 479:2~6, 수호신 폼). 트레이너 카드의 다른 면(실버·골드 = 해금 단계)은
    /// 키가 따로 없어 직업 색인에 면 수(2)를 센다. 이미지 전용 카드는 다른 면이 없다.
    /// </summary>
    public sealed class DeckDef
    {
        public readonly string Key;
        public readonly DeckKind Kind;
        public readonly IReadOnlyList<DeckEntry> Cards;
        public readonly IReadOnlyList<DeckEntry> AltStates;

        public DeckDef(string key, DeckKind kind, IReadOnlyList<DeckEntry> cards, IReadOnlyList<DeckEntry> altStates)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Cards = cards ?? throw new ArgumentNullException(nameof(cards));
            AltStates = altStates ?? throw new ArgumentNullException(nameof(altStates));
            if (cards.Count == 0) throw new ArgumentException($"{key}: 빈 덱", nameof(cards));
            if (kind == DeckKind.Card && altStates.Count > 0) throw new ArgumentException($"{key}: 이미지 전용 카드에는 다른 면이 없다", nameof(altStates));
            Kind = kind;
        }

        /// <summary>바탕 카드 장수 합.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (var e in Cards) n += e.Copies;
                return n;
            }
        }

        /// <summary>다른 면 수 합.</summary>
        public int AltStateCount
        {
            get
            {
                int n = 0;
                foreach (var e in AltStates) n += e.Copies;
                return n;
            }
        }
    }

    /// <summary>
    /// 이미지 전용 카드 앞면 하나(cards.json 행). 문구가 그림에만 있는 카드(사당 이벤트·운명의 변화·왜곡 균열·은행상회·
    /// 고향 마을·막·울트라 온슬롯)다. 키 = "&lt;덱&gt;.&lt;영문 이름 snake&gt;", 이름이 없는 앞면(은행상회·온슬롯)은 앞면 번호.
    /// M5 「카드·아이템 데이터」(03 §1-3·§1-4): 영문 카드 그림을 보고 옮긴 본문(En — 효과의 정본)·은행상회 거래(Trades)·출처(SrcKo·SrcEn)·대조한 날짜(Verified).
    /// 효과 구현은 그 카드를 쓰는 묶음이 한다(효과 표의 키 = 이 카드 키).
    /// </summary>
    public sealed class CardDef
    {
        public readonly string Key;
        /// <summary>덱 색인(DeckDb.Decks).</summary>
        public readonly int Deck;
        public readonly int Copies;
        /// <summary>영문 본문(카드 그림의 줄 순서, 제목 제외). 글이 없는 은행상회 카드는 null(Trades가 있다).</summary>
        public readonly IReadOnlyList<string> En;
        /// <summary>은행상회 거래 두 줄(위 → 아래). 다른 카드는 null.</summary>
        public readonly IReadOnlyList<CardTrade> Trades;
        /// <summary>한국어 전사본 위치(rules-summary 절·한국어판 시트).</summary>
        public readonly string SrcKo;
        /// <summary>영문 출처(원작 GitHub 그림 경로#시트 칸, 룰북 행).</summary>
        public readonly string SrcEn;
        /// <summary>영문 그림과 대조한 날짜(YYYY-MM-DD).</summary>
        public readonly string Verified;

        public CardDef(string key, int deck, int copies, IReadOnlyList<string> en, IReadOnlyList<CardTrade> trades, string srcKo, string srcEn, string verified)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies), copies, $"{key}: 장수는 1 이상");
            if ((en == null) == (trades == null)) throw new ArgumentException($"{key}: 영문 본문과 거래 가운데 하나만 있어야 한다");
            if (en != null)
            {
                if (en.Count == 0) throw new ArgumentException($"{key}: 빈 본문", nameof(en));
                var a = new string[en.Count];
                for (int i = 0; i < a.Length; i++) a[i] = !string.IsNullOrEmpty(en[i]) ? en[i] : throw new ArgumentException($"{key}: 본문 {i}째 줄이 비었다", nameof(en));
                en = a;
            }
            if (trades != null)
            {
                if (trades.Count != 2) throw new ArgumentException($"{key}: 거래는 두 줄", nameof(trades));
                var a = new CardTrade[2];
                for (int i = 0; i < 2; i++) a[i] = trades[i] ?? throw new ArgumentException($"{key}: 거래 {i}가 null이다", nameof(trades));
                trades = a;
            }
            if (string.IsNullOrEmpty(srcKo) || string.IsNullOrEmpty(srcEn)) throw new ArgumentException($"{key}: 출처(src) 빈칸");
            if (string.IsNullOrEmpty(verified)) throw new ArgumentException($"{key}: verified 빈칸(영문 그림과 대조한 날짜)");
            Deck = deck;
            Copies = copies;
            En = en;
            Trades = trades;
            SrcKo = srcKo;
            SrcEn = srcEn;
            Verified = verified;
        }
    }

    /// <summary>은행상회 거래 한 줄(카드 그림 왼쪽부터): Give를 내고 Get을 받는다(S11, rulings A4).</summary>
    public sealed class CardTrade
    {
        public readonly IReadOnlyList<ApricornColor> Give;
        public readonly IReadOnlyList<ApricornColor> Get;

        public CardTrade(IReadOnlyList<ApricornColor> give, IReadOnlyList<ApricornColor> get)
        {
            if (give == null || give.Count == 0) throw new ArgumentException("거래: 내는 규토리가 없다", nameof(give));
            if (get == null || get.Count == 0) throw new ArgumentException("거래: 받는 규토리가 없다", nameof(get));
            var g = new ApricornColor[give.Count];
            for (int i = 0; i < g.Length; i++) g[i] = give[i];
            var t = new ApricornColor[get.Count];
            for (int i = 0; i < t.Length; i++) t[i] = get[i];
            Give = g;
            Get = t;
        }
    }

    /// <summary>
    /// 덱과 이미지 전용 카드 표(정적 데이터). 읽기는 Sinnoh.Data DeckDbLoader(포켓몬·트레이너 카드 색인은 CardDb 기준).
    /// 이미지 카드 덱의 항목과 카드 행(덱·장수)이 서로 맞는지 확인한다.
    /// </summary>
    public sealed class DeckDb
    {
        public readonly IReadOnlyList<DeckDef> Decks;
        public readonly IReadOnlyList<CardDef> Cards;
        public readonly IdTable DeckIds;
        public readonly IdTable CardIds;

        public DeckDb(IReadOnlyList<DeckDef> decks, IReadOnlyList<CardDef> cards)
        {
            if (decks == null) throw new ArgumentNullException(nameof(decks));
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            var d = new DeckDef[decks.Count];
            var dk = new string[d.Length];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = decks[i] ?? throw new ArgumentException($"덱 행 {i}이 null이다", nameof(decks));
                dk[i] = d[i].Key;
            }
            var c = new CardDef[cards.Count];
            var ck = new string[c.Length];
            var listed = new int[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                c[i] = cards[i] ?? throw new ArgumentException($"카드 행 {i}이 null이다", nameof(cards));
                if ((uint)c[i].Deck >= (uint)d.Length || d[c[i].Deck].Kind != DeckKind.Card)
                    throw new ArgumentException($"{c[i].Key}: 덱 색인 {c[i].Deck}이 이미지 카드 덱이 아니다");
                ck[i] = c[i].Key;
            }
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i].Kind != DeckKind.Card) continue;
                foreach (var e in d[i].Cards)
                {
                    if (e.Card >= c.Length || c[e.Card].Deck != i || c[e.Card].Copies != e.Copies)
                        throw new ArgumentException($"{d[i].Key}: 카드 {e.Card}의 덱·장수가 cards 행과 다르다");
                    listed[e.Card]++;
                }
            }
            for (int i = 0; i < c.Length; i++)
                if (listed[i] != 1) throw new ArgumentException($"{c[i].Key}: 덱 항목에 {listed[i]}번 나온다");
            Decks = d;
            Cards = c;
            DeckIds = new IdTable("decks", dk);
            CardIds = new IdTable("cards", ck);
        }
    }
}
