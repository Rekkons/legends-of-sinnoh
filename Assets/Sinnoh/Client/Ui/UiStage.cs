using System;
using System.Text.RegularExpressions;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// PC 층 화면 하나(04 §2 S번호 하나). <see cref="Build"/>가 패널을 덮는 요소 하나를 만들어 돌려주고, 자리·크기는 USS(<c>Styles~/Sinnoh.uss</c>)가 정한다 —
    /// 등급이 바뀌면 뿌리의 토큰 벌만 바뀌므로 다시 만들지 않는다. 패널이 뿌리를 다시 주면(다시 읽기 — 화면 뿌리를 껐다 켤 때처럼 같은 뿌리로도 온다)
    /// <see cref="Close"/> 뒤에 <see cref="Build"/>가 한 번 더 불린다 — <see cref="Build"/>와 <see cref="Close"/>는 늘 짝이다.
    /// </summary>
    public interface IUiScreen
    {
        /// <summary>바뀌지 않는 화면 이름(캡처·튜토리얼 앵커, 결정 38). 돌려주는 요소의 <c>name</c>과 같다.</summary>
        string Name { get; }

        VisualElement Build(UiStage stage);

        /// <summary>화면이 닫혔다(요소는 이미 떼었다). <see cref="Build"/>에서 걸어 둔 트윈·구독을 푼다. 뒤에 <see cref="Build"/>가 다시 불릴 수 있다.</summary>
        void Close();
    }

    /// <summary>
    /// 화면이 얹히는 자리: 패널 뿌리 요소 + 겉모양 + 트윈 실행기(화면에 하나, 04 §8-6) + 지금 열린 화면 하나.
    /// 층은 넷이다(아래 → 위): 밑층(<see cref="Under"/> — 보드 위 도트 이펙트, 없을 수 있다) · 늘 떠 있는 판(<see cref="Hud"/> — 보드 HUD) · 그 위에 여는 화면 하나(<see cref="Current"/> — 창·메뉴·셋업) ·
    /// 그 위에 덮는 정보 창 하나(<see cref="Info"/> — 결정 125, 없을 수 있다) · 툴팁.
    /// 엔진 루프를 모른다 — 실행 중에는 <see cref="ScreenRoot"/>가 패널을 만들어 <see cref="Bind"/>하고 프레임마다 <see cref="Tweens"/>를 돌리며,
    /// 배치 캡처(<c>Sinnoh.Editor.UiShots</c>)는 창 없는 패널의 뿌리를 <see cref="Bind"/>한다.
    /// </summary>
    public sealed class UiStage
    {
        public readonly KitSkin Skin;
        public readonly TweenRunner Tweens = new TweenRunner();

        /// <summary>
        /// 화면 문자열 표(strings.ko · strings.en — <c>screen.*</c>·이름·카드 문구). 화면은 글을 코드에 적지 않고 <see cref="T"/>로 읽는다. 표 없이 만든 무대(키트 갤러리·시험)면 null.
        /// 언어를 바꾸면(<see cref="UseLang"/> — M9 언어 ②) 다른 표로 바뀐다.
        /// </summary>
        public StringTable Strings { get; private set; }

        /// <summary>지금 표의 언어(<c>ko</c> · <c>en</c>). 표가 없으면 null.</summary>
        public string Lang => Strings?.Lang;

        /// <summary>언어 → 그 언어의 문자열 표(없으면 null). 화면 시작점(<see cref="ScreenBoot"/>)·캡처가 넣는다. 없으면 언어를 바꿀 수 없다.</summary>
        public Func<string, StringTable> Tables { get; set; }

        /// <summary>그 언어의 표(지금 표의 언어면 지금 표, 없으면 null).</summary>
        public StringTable TableOf(string lang) => lang == null ? null : lang == Lang ? Strings : Tables?.Invoke(lang);

        /// <summary>
        /// 화면 글의 언어를 바꾼다(M9 언어 ② — S02 「언어」): 그 언어의 표로 바꾸고 열린 판(<see cref="Hud"/>)·화면(<see cref="Current"/>)을 닫았다 다시 만든다(뿌리를 다시 받을 때와 같은 짝 — 툴팁 · 밑층 · 윗층은 그대로).
        /// 무대 밖에 붙은 층(튜토리얼 · 방 층)은 부른 쪽(<see cref="GameFlow.Lang"/>)이 다시 쓴다. 이미 그 언어거나 표가 없으면 아무 일도 없다(false = 표가 없다).
        /// </summary>
        public bool UseLang(string lang)
        {
            if (lang == null) return false;
            if (lang == Lang) return true;
            var table = Tables?.Invoke(lang);
            if (table == null) return false;
            Strings = table;
            IconArt.Lang = Lang;   // 영문 타입 · 상태 그림(결정 115)
            if (Root != null)
            {
                MarkLang();
                Tips?.Hide();
                if (currentElement != null)
                {
                    currentElement.RemoveFromHierarchy();
                    currentElement = null;
                    Current.Close();
                }
                if (hudElement != null)
                {
                    hudElement.RemoveFromHierarchy();
                    hudElement = null;
                    Hud.Close();
                }
                if (infoElement != null)
                {
                    infoElement.RemoveFromHierarchy();
                    infoElement = null;
                    Info.Close();
                }
                if (Hud != null) ShowHud();
                if (Current != null) Show();
                if (Info != null) ShowInfo();
            }
            return true;
        }

        public VisualElement Root { get; private set; }
        public UiGrade Grade { get; private set; }
        public KitTooltip Tips { get; private set; }
        public IUiScreen Current { get; private set; }

        /// <summary>화면 아래에 늘 떠 있는 판(보드 HUD). 없으면 null. 화면(<see cref="Current"/>)을 열고 닫아도 그대로 있다.</summary>
        public IUiScreen Hud { get; private set; }

        /// <summary>
        /// 화면(<see cref="Current"/>) 위에 덮어 여는 정보 창(결정 125 — 정비 · 도감 · 트레이너 카드 · 운명의 변화 · 설명서 · 상성표 · 게임 메뉴를 물음 창 · 배틀 · 결과 창 위에서 연다). 없으면 null.
        /// 아래 화면은 그대로 남고 그사이 바뀌어도(열고 닫혀도) 이 창은 그대로다. 윗층(<see cref="Over"/>)·툴팁보다는 아래.
        /// </summary>
        public IUiScreen Info { get; private set; }

        VisualElement currentElement, hudElement, infoElement;

        /// <summary>판(<see cref="Hud"/>)보다 아래의 층(보드 위에 바로 그리는 것 — 도트 이펙트 판). 없으면 null. 판·화면을 열고 닫아도, 뿌리를 다시 받아도 늘 맨 아래다.</summary>
        public VisualElement Under { get; private set; }

        /// <summary>밑층을 놓는다(있던 것은 뗀다). null이면 떼기만 한다.</summary>
        public void SetUnder(VisualElement under)
        {
            if (!ReferenceEquals(Under, under)) Under?.RemoveFromHierarchy();
            Under = under;
            if (Root != null && under != null && (under.parent != Root || Root.IndexOf(under) != 0)) Root.Insert(0, under);
        }

        /// <summary>
        /// 화면(<see cref="Current"/>)보다 위, 툴팁보다 아래의 층(창·배틀 화면 위에 덮는 연출 — 여행 스탬프 · 도전 VS · 거래, M5 「스탬프·도전·거래 화면」). 없으면 null.
        /// 마우스를 받지 않는 요소만 둔다. 판·화면을 열고 닫아도, 뿌리를 다시 받아도 늘 화면 위다.
        /// </summary>
        public VisualElement Over { get; private set; }

        /// <summary>윗층을 놓는다(있던 것은 뗀다). null이면 떼기만 한다.</summary>
        public void SetOver(VisualElement over)
        {
            if (!ReferenceEquals(Over, over)) Over?.RemoveFromHierarchy();
            Over = over;
            if (Root == null || over == null) return;
            // 툴팁 층 바로 아래로(판·화면은 그보다 아래에 끼워진다 — Show·ShowHud). 그 위에 얹힌 것(가운데 필드 전환 줄무늬)의 자리는 건드리지 않는다
            bool tips = Tips != null && Tips.Element.parent == Root;
            if (over.parent != Root) Root.Add(over);
            if (tips && Root.IndexOf(over) != Root.IndexOf(Tips.Element) - 1) over.PlaceBehind(Tips.Element);
        }

        // 밑층이 뿌리에 있으면 판·화면은 그 위부터다
        int Floor => Under != null && Under.parent == Root ? 1 : 0;

        // ── 즉시 소리(04 §8-4 「즉시 소리(내 클릭)와 연출 소리를 나눈다」) ──

        /// <summary>
        /// 내 클릭의 즉시 소리 키(<see cref="SoundKeys"/>)를 받는 곳 — 화면 흐름(<see cref="GameFlow"/>)이 만들 때 자기를 건다(받는 곳은 하나 — 마지막에 만든 흐름). 연출 소리(대기열 단서)와 따로 간다.
        /// 키트 단추는 뿌리가 알아서 낸다: 뒤로·닫기·취소(이름에 back·close·cancel·no 마디) = 무음(결정 70 ⑯), 나머지 = 확정, 꺼진 단추·흐린 단추(<see cref="OffClass"/>) = 무음.
        /// 단추가 아닌 누를 칸(카드·줄·칩)은 화면이 <see cref="OnTap"/>으로 건다. 화면 함수를 바로 부른 것(확인용 길잡이·시험)은 소리가 없다 — 사람의 클릭만.
        /// </summary>
        public Action<string> TapSink { get; set; }

        /// <summary>즉시 소리 하나를 알린다.</summary>
        public void Tap(string key)
        {
            if (!string.IsNullOrEmpty(key)) TapSink?.Invoke(key);
        }

        /// <summary>
        /// 단추가 아닌 누를 칸에 클릭을 건다: 켜진 칸을 누르면 즉시 소리 <paramref name="key"/>(고르기 = 고르기 소리 · 답 = 확정)를 낸 뒤 <paramref name="act"/>.
        /// 칸을 끄지 않고 핸들러 안에서만 입력을 막는 화면(셋업 고르기 — 연출 동안 · 답을 낸 뒤)은 <paramref name="open"/>을 준다: 거짓이면 무음.
        /// </summary>
        public void OnTap(VisualElement element, string key, Action<ClickEvent> act, Func<bool> open = null)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (act == null) throw new ArgumentNullException(nameof(act));
            element.RegisterCallback<ClickEvent>(e =>
            {
                if (element.enabledInHierarchy && (open == null || open())) Tap(key);
                act(e);
            });
        }

        static readonly Regex CancelName = new Regex("(^|-)(back|close|cancel|no)(-|$)");

        /// <summary>흐린 단추의 USS 클래스: 툴팁을 띄우려고 켜 둔 채 눌러도 아무 일이 없는 단추(배틀 [가방]·[기권] · 마지막 한 마리의 [놓아주기] · 준비 중 HUD 단추).</summary>
        public const string OffClass = "sn-hud-off";

        /// <summary>그 단추의 즉시 소리(꺼진 단추 · 흐린 단추 · 뒤로·닫기·취소 = null — 결정 70 ⑯): 나머지 = 확정.</summary>
        public static string ButtonSound(Button button) =>
            button == null || !button.enabledInHierarchy || button.ClassListContains(OffClass) || CancelName.IsMatch(button.name ?? "") ? null
            : FoldName.IsMatch(button.name ?? "") ? SoundKeys.Select : SoundKeys.Decide;

        // 창 [접기]·[펼치기](결정 87 V19 ①)는 답이 아니라 보기만 바꾼다 — 「확정」 대신 「선택」 소리(임시)
        static readonly Regex FoldName = new Regex("(^|-)fold(-|$)");

        /// <summary>
        /// 키보드 「제출」(UI Toolkit의 NavigationSubmitEvent — Enter · 설정에 따라 Space)로 포커스된 단추가 눌리지 않게 뿌리에서 막는다. 게임 단추는 마우스로만 누르고,
        /// 키는 ScreenBoot가 한 곳에서 읽는다(Esc · 스페이스 바 진화 건너뛰기 · 숫자 — 스페이스 바가 건너뛰기와 마지막에 누른 단추를 함께 누르던 틈, M6 화면 교차 검토 확인 A).
        /// </summary>
        public static void NoSubmit(NavigationSubmitEvent e) => e.StopImmediatePropagation();

        // 단추를 누른 순간(왼쪽 단추): 뿌리에서 먼저(내려가는 길) 본다 [가정 — plan §5 #55]. 뗄 때가 아닌 까닭: 단추는 뗄 때 제 일을 해 화면을 닫기도 해서(뒤로·닫기)
        // 그 뒤의 ClickEvent가 오지 않고, 뗄 때는 단추가 누름을 잡고 있어(pointer capture) 뿌리를 거치지 않는다. 눌렀다 단추 밖에서 떼어도 소리는 이미 났다
        void RootPressed(PointerDownEvent e)
        {
            if (e.button != 0) return;
            for (var el = e.target as VisualElement; el != null && el != Root; el = el.parent)
                if (el is Button button)
                {
                    Tap(ButtonSound(button));
                    return;
                }
        }

        /// <summary>뿌리의 언어 클래스(<c>sn-lang-ko</c> · <c>sn-lang-en</c>): 영어 글이 길어 칸이 모자란 곳만 USS가 그 언어에서 꼬리표를 빼거나 칸을 넓힌다(M9 언어 ②).</summary>
        public const string LangClass = "sn-lang-";

        void MarkLang()
        {
            foreach (string l in new[] { "ko", "en" }) Root.EnableInClassList(LangClass + l, l == Lang);
        }

        public UiStage(KitSkin skin, StringTable strings = null)
        {
            Skin = skin ?? throw new ArgumentNullException(nameof(skin));
            Strings = strings;
            IconArt.Lang = Lang;
        }

        /// <summary>문자열 표의 글. 표가 없거나 키가 없으면 던진다 — 빠진 글을 빈 칸으로 넘기지 않는다.</summary>
        public string T(string key)
        {
            if (Strings == null) throw new InvalidOperationException("문자열 표 없이 만든 무대다: " + key);
            return Strings[key];
        }

        /// <summary>있을 수도 없을 수도 있는 글(설명 둘째 줄처럼). 없으면 null.</summary>
        public string TOrNull(string key) => Strings != null && Strings.TryGet(key, out var text) ? text : null;

        /// <summary>
        /// 패널 뿌리를 받는다(처음, 그리고 패널이 뿌리를 다시 줄 때마다 — 같은 뿌리로도 온다). 앞서 얹은 화면 요소·툴팁은 떼고, 열려 있던 화면은 닫았다가 다시 만든다.
        /// </summary>
        public void Bind(VisualElement root, UiGrade grade)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (Tips != null)
            {
                Tips.Hide();
                Tips.Element.RemoveFromHierarchy();
            }
            if (currentElement != null)
            {
                currentElement.RemoveFromHierarchy();
                currentElement = null;
                Current.Close();
            }
            if (hudElement != null)
            {
                hudElement.RemoveFromHierarchy();
                hudElement = null;
                Hud.Close();
            }
            if (infoElement != null)
            {
                infoElement.RemoveFromHierarchy();
                infoElement = null;
                Info.Close();
            }
            if (Root != null)
            {
                Root.UnregisterCallback<PointerDownEvent>(RootPressed, TrickleDown.TrickleDown);
                Root.UnregisterCallback<NavigationSubmitEvent>(NoSubmit, TrickleDown.TrickleDown);
            }
            Root = root;
            Grade = grade;
            root.RegisterCallback<PointerDownEvent>(RootPressed, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(NoSubmit, TrickleDown.TrickleDown);
            // 테마가 비어도(뿌리 높이 0, 04 §9-6) 화면이 무너지지 않게 뿌리를 패널에 맞춘다
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            UiSkin.Attach(root, Skin, grade);
            MarkLang();
            Tips = new KitTooltip(root, Skin);
            SetUnder(Under);
            if (Hud != null) ShowHud();
            if (Current != null) Show();
            if (Info != null) ShowInfo();
            SetOver(Over);
        }

        /// <summary>창 크기가 바뀌어 등급이 달라졌다: 토큰 벌만 바꾼다(패널 배율은 패널을 가진 쪽이 넣는다).</summary>
        public void SetGrade(UiGrade grade)
        {
            if (grade.Equals(Grade)) return;
            Grade = grade;
            if (Root != null) UiSkin.Attach(Root, Skin, grade);
        }

        /// <summary>화면을 연다. 열려 있던 화면은 닫는다(한 번에 하나). 뿌리를 아직 못 받았으면 받는 대로 만든다.</summary>
        public void Open(IUiScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            Shut();
            Current = screen;
            if (Root != null) Show();
        }

        /// <summary>열린 화면을 닫는다.</summary>
        public void Shut()
        {
            if (Current == null) return;
            Tips?.Hide();
            currentElement?.RemoveFromHierarchy();
            currentElement = null;
            var was = Current;
            Current = null;
            was.Close();
        }

        /// <summary>늘 떠 있는 판을 연다(있던 판은 닫는다). 화면(<see cref="Current"/>)은 건드리지 않는다 — 판은 화면 아래에 깔린다.</summary>
        public void OpenHud(IUiScreen hud)
        {
            if (hud == null) throw new ArgumentNullException(nameof(hud));
            ShutHud();
            Hud = hud;
            if (Root != null) ShowHud();
        }

        /// <summary>늘 떠 있는 판을 닫는다.</summary>
        public void ShutHud()
        {
            if (Hud == null) return;
            Tips?.Hide();
            hudElement?.RemoveFromHierarchy();
            hudElement = null;
            var was = Hud;
            Hud = null;
            was.Close();
        }

        /// <summary>정보 창을 화면 위에 연다(있던 정보 창은 닫는다). 화면(<see cref="Current"/>)은 건드리지 않는다.</summary>
        public void OpenInfo(IUiScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            ShutInfo();
            Info = screen;
            if (Root != null) ShowInfo();
        }

        /// <summary>정보 창을 닫는다(아래 화면은 그대로).</summary>
        public void ShutInfo()
        {
            if (Info == null) return;
            Tips?.Hide();
            infoElement?.RemoveFromHierarchy();
            infoElement = null;
            var was = Info;
            Info = null;
            was.Close();
        }

        void ShowInfo()
        {
            infoElement = Info.Build(this);
            infoElement.name = Info.Name;
            infoElement.AddToClassList("sn-screen");
            Root.Insert(Floor + (hudElement != null ? 1 : 0) + (currentElement != null ? 1 : 0), infoElement);   // 화면 위, 윗층·툴팁 층 아래
        }

        void Show()
        {
            currentElement = Current.Build(this);
            currentElement.name = Current.Name;
            currentElement.AddToClassList("sn-screen");
            Root.Insert(Floor + (hudElement != null ? 1 : 0), currentElement);   // 판 위, 윗층·툴팁 층 아래
        }

        void ShowHud()
        {
            hudElement = Hud.Build(this);
            hudElement.name = Hud.Name;
            hudElement.AddToClassList("sn-screen");
            Root.Insert(Floor, hudElement);   // 맨 아래(밑층 바로 위)
        }
    }
}
