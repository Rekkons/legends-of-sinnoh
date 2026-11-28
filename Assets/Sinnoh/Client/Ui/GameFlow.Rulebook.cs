using System;
using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    // 룰 설명서 S43(결정 100 · 111 ① — 큰 창 B안 · 게임 중 옆 판 C안)과 크레딧 S03(M9 「설명서 화면 S43 · 크레딧 S03」). 게임 흐름 본문(GameFlow.cs)과 따로 둔 몫.
    public sealed partial class GameFlow
    {
        /// <summary>
        /// 화면이 읽는 생성 데이터(`Data/Json` 아래 파일 이름 → 글 — 없으면 null): 설명서 <c>rulebook.&lt;언어&gt;.json</c> · 크레딧 <c>credits.json</c>.
        /// 에디터는 에셋 데이터베이스에서 읽고, 플레이어는 보드 씬의 <see cref="ScreenBoot"/>가 씬이 가리키는 TextAsset으로 넣는다(튜토리얼 각본과 같은 길 — <see cref="TutorialMenu.ReadJson"/>).
        /// </summary>
        public Func<string, string> ReadBook
        {
            get => readBook ?? DefaultReadBook;
            set
            {
                readBook = value;
                books.Clear();
                rulings.Clear();
                credits = null;
            }
        }

        Func<string, string> readBook;
        readonly Dictionary<string, Rulebook> books = new Dictionary<string, Rulebook>(StringComparer.Ordinal);
        readonly Dictionary<string, Rulings> rulings = new Dictionary<string, Rulings>(StringComparer.Ordinal);
        CreditsTable credits;
        string bookLast;
        RulebookSide bookSide;

        static string DefaultReadBook(string file)
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Sinnoh/Data/Json/" + file);
            return asset != null ? asset.text : null;
#else
            return null;
#endif
        }

        /// <summary>설명서 언어 = 게임 언어(화면 문자열 표의 언어 — plan §5 #304 · 결정 111 ②). 표가 없으면 한국어.</summary>
        public string BookLang => Stage.Strings != null && !string.IsNullOrEmpty(Stage.Strings.Lang) ? Stage.Strings.Lang : "ko";

        /// <summary>그 언어의 설명서(한 번 읽어 둔다). 데이터가 없으면 null.</summary>
        public Rulebook Book(string lang)
        {
            if (books.TryGetValue(lang, out var b)) return b;
            string file = "rulebook." + lang + ".json";
            string json = ReadBook(file);
            b = json != null ? Rulebook.Load(json, file) : null;
            books[lang] = b;
            return b;
        }

        /// <summary>크레딧 표(한 번 읽어 둔다). 데이터가 없으면 null.</summary>
        public CreditsTable Credits()
        {
            if (credits != null) return credits;
            string json = ReadBook("credits.json");
            return credits = json != null ? CreditsTable.Load(json) : null;
        }

        /// <summary>마지막으로 본 절(다시 열 때 그 절 — 실행하는 동안만 쥔다 [가정 plan §5 #371]).</summary>
        public string BookLast => bookLast;

        /// <summary>타이틀의 「룰 설명서」 줄: 큰 창을 연다(하던 게임은 떠난다). [뒤로]는 타이틀의 그 줄로.</summary>
        public void OpenTitleRulebook()
        {
            var book = Book(BookLang);
            if (book == null) return;
            Leave();
            Stage.Open(new RulebookScreen(book, Ctx, BookLang, bookLast, CloseRulebook, false, k => bookLast = k));
            Restage();
        }

        /// <summary>
        /// 게임 중 큰 창(게임 메뉴 [룰 설명서] · 옆 판 [크게 보기] · 창이 떠 있을 때의 위 띠 [?]): 보드에 있으면 언제든(결정 125 — <see cref="CanPeek"/>) — 도감처럼 화면 대기열은 멈추지 않는다.
        /// <paramref name="key"/> = 처음 볼 절(null = 마지막으로 본 절).
        /// </summary>
        public bool OpenGameRulebook(string key = null)
        {
            var book = Book(BookLang);
            if (book == null || !CanPeek) return false;
            if (Front is RulebookScreen) return true;
            // 창 · 배틀 · 결과 창이 떠 있으면 그 위 정보 창 층에(결정 125 — 아래 창은 그대로)
            ShowInfo(new RulebookScreen(book, Ctx, BookLang, key ?? bookLast, CloseRulebook, true, k => bookLast = k), Stage.Current == null);
            return true;
        }

        /// <summary>게임 메뉴의 [룰 설명서]: 메뉴를 닫고 큰 창을 연다(보기만 하는 창 — 메뉴처럼 화면 대기열을 세우지 않는다 [가정 plan §5 #372]).</summary>
        public bool OpenMenuRulebook()
        {
            if (!(MenuLayer is BoardMenuScreen)) return false;
            CloseMenu();
            return OpenGameRulebook();
        }

        /// <summary>큰 창의 [닫기]·[뒤로](= Esc): 게임 중이면 보드로, 타이틀에서 열었으면 타이틀로.</summary>
        public void CloseRulebook()
        {
            if (ShutInfo(Stage.Info is RulebookScreen)) return;
            if (!(Stage.Current is RulebookScreen book)) return;
            if (!book.InGame)
            {
                OpenTitle(TitleScreen.Rulebook);
                return;
            }
            Stage.Shut();
            Sync();
        }

        /// <summary>그 언어의 「규칙 판정」 표(결정 213 — <c>rulings.&lt;언어&gt;.json</c>, 한 번 읽어 둔다). 데이터가 없으면 null.</summary>
        public Rulings RulingsOf(string lang)
        {
            if (rulings.TryGetValue(lang, out var t)) return t;
            string file = "rulings." + lang + ".json";
            string json = ReadBook(file);
            t = json != null ? Rulings.Load(json, file) : null;
            rulings[lang] = t;
            return t;
        }

        /// <summary>타이틀의 「규칙 판정」 줄: 화면을 연다(하던 게임은 떠난다). [뒤로]는 타이틀의 그 줄로.</summary>
        public void OpenTitleRulings()
        {
            var table = RulingsOf(BookLang);
            if (table == null) return;
            Leave();
            Stage.Open(new RulingsScreen(table, CloseRulings, false));
            Restage();
        }

        /// <summary>게임 중 「규칙 판정」(게임 메뉴 [규칙 판정]): 룰 설명서 큰 창과 같은 길 — 보드에 있으면 언제든, 화면 대기열은 멈추지 않는다.</summary>
        public bool OpenGameRulings()
        {
            var table = RulingsOf(BookLang);
            if (table == null || !CanPeek) return false;
            if (Front is RulingsScreen) return true;
            ShowInfo(new RulingsScreen(table, CloseRulings, true), Stage.Current == null);
            return true;
        }

        /// <summary>게임 메뉴의 [규칙 판정]: 메뉴를 닫고 화면을 연다.</summary>
        public bool OpenMenuRulings()
        {
            if (!(MenuLayer is BoardMenuScreen)) return false;
            CloseMenu();
            return OpenGameRulings();
        }

        /// <summary>「규칙 판정」의 [닫기]·[뒤로](= Esc): 게임 중이면 보드로, 타이틀에서 열었으면 타이틀의 그 줄로.</summary>
        public void CloseRulings()
        {
            if (ShutInfo(Stage.Info is RulingsScreen)) return;
            if (!(Stage.Current is RulingsScreen r)) return;
            if (!r.InGame)
            {
                OpenTitle(TitleScreen.Rulings);
                return;
            }
            Stage.Shut();
            Sync();
        }

        /// <summary>타이틀의 「크레딧」 줄: 크레딧 화면을 연다. [뒤로]는 타이틀의 그 줄로.</summary>
        public void OpenCredits()
        {
            var table = Credits();
            if (table == null) return;
            Leave();
            Stage.Open(new CreditsScreen(table, CloseCredits));
            Restage();
        }

        /// <summary>크레딧의 [뒤로](= Esc): 타이틀의 「크레딧」 줄로.</summary>
        public void CloseCredits()
        {
            if (Stage.Current is CreditsScreen) OpenTitle(TitleScreen.Credits);
        }

        /// <summary>게임 중 옆 판(C안 — 처음 열 때 만든다).</summary>
        public RulebookSide BookSide
        {
            get
            {
                if (bookSide == null)
                {
                    bookSide = new RulebookSide(this);
                    Ticked += bookSide.Sync;
                }
                return bookSide;
            }
        }

        /// <summary>위 띠의 [설명서]: 옆 판을 열고 닫는다(보드에 있을 때만 연다). 열렸으면 true.</summary>
        public bool ToggleBookSide()
        {
            if (bookSide != null && bookSide.IsOpen)
            {
                bookSide.Close();
                return false;
            }
            if (!OnBoard || Book(BookLang) == null) return false;
            if (Stage.Current != null || Stage.Info != null) return OpenGameRulebook();   // 창 · 배틀 위에서는 옆 판이 그 아래에 가린다 — 큰 창을 정보 창 층에(결정 125)
            BookSide.Open();
            return true;
        }

        // Esc(GameFlow.Escape의 맨 앞): 큰 창 · 크레딧은 타이틀에서 연 것도 닫는다. 옆 판은 창이 아니라 받지 않는다 [가정 plan §5 #372]
        bool EscapeBook()
        {
            if (Stage.Current is RulebookScreen || Stage.Info is RulebookScreen)
            {
                CloseRulebook();
                return true;
            }
            if (Stage.Current is RulingsScreen || Stage.Info is RulingsScreen)
            {
                CloseRulings();
                return true;
            }
            if (Stage.Current is CreditsScreen)
            {
                CloseCredits();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 옆 판 칩의 상황 이름(<see cref="RulebookChips"/> — 앞이 먼저): 맨 위 창 → 내 칸의 고정물 · 토큰 · 같은 칸의 다른 트레이너 → 캠페인 → 늘 붙는 턴 행동.
        /// 보이는 정보만 쓴다(뒷면 숨겨진 장소는 「숨겨진 장소」로만 — 정체를 보지 않는다).
        /// </summary>
        public IEnumerable<string> BookSituations()
        {
            switch (Top)
            {
                case EncounterScreen _: yield return "screen.encounter"; break;
                case BattleScreen _: yield return "screen.battle"; break;
                case CraftScreen _: yield return "screen.craft"; break;
                case EvolveScreen _: yield return "screen.evolve"; break;
                case PartyScreen _:
                case MonScreen _: yield return "screen.party"; break;
                case StampScreen _: yield return "screen.stamp"; break;
                case FateScreen _: yield return "screen.fate"; break;
                case ShrineScreen _: yield return "screen.shrine"; break;
                case TradeScreen _:
                case TradePickScreen _: yield return "screen.trade"; break;
                case ChallengeScreen _: yield return "screen.challenge"; break;
                case VillageScreen _: yield return "screen.village"; break;
                case BreachScreen _: yield return "screen.breach"; break;
                case BlessScreen _: yield return "screen.bless"; break;
            }
            var view = Client != null ? Client.View : null;
            if (view != null && Client.Seat >= 0 && Client.Seat < view.Seats.Length)
            {
                int cell = view.Seats[Client.Seat].Cell;
                if (cell >= 0 && Ctx != null)
                {
                    foreach (var f in Ctx.Fixtures.Fixtures)
                        if (f.Cell == cell) yield return "cell." + Snake(f.Kind.ToString());
                    foreach (var t in view.Tokens)
                    {
                        if (t.Cell != cell || t.Destroyed) continue;
                        if (t.Kind == TokenKind.HiddenSpot) yield return "cell.hidden_spot";
                        else if (t.Kind == TokenKind.Grunt) yield return "cell.grunt";
                        else if (t.Kind == TokenKind.UltraBeast || t.Kind == TokenKind.Necrozma) yield return "cell.ultra_beast";
                    }
                    for (int s = 0; s < view.Seats.Length; s++)
                        if (s != Client.Seat && view.Seats[s].Cell == cell) yield return "cell.rival";
                }
                if (view.Campaign == Campaign.UltraBurst) yield return "campaign.ultra_burst";
            }
            yield return RulebookChips.Turn;
        }

        static string Snake(string pascal)
        {
            var sb = new System.Text.StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }
    }
}
