# Rekkon UI Kit (`com.rekkon.uikit`)

Unity UI Toolkit으로 만드는 PC 화면의 공용 부품 패키지입니다. 게임 하나에 묶이지 않는 부품만 들어 있고, 글꼴 파일 · 그림 · 게임 전용 화면은 넣지 않습니다. 이 게임에서는 `Packages/` 안의 내장 패키지로 씁니다.

- Unity 6000.6. 다른 프로젝트에서 쓰려면 이 폴더를 그 프로젝트의 `Packages/`에 두거나 `Packages/manifest.json`에 `file:` 경로로 적습니다(시험을 돌리려면 `testables`에도).
- 겉모양은 도트 풍입니다(단색, 모서리 0, 단추 아래 테두리로 그린 딱딱한 그림자, 도트 글꼴 12/24px). 토큰 값은 `Styles~/Kit.uss` 한 곳에 있습니다.

## 들어 있는 것

| 무엇 | 어디 | 메모 |
|---|---|---|
| 해상도 등급 `UiGrade` | Core | 토큰 벌(720·1080) × 패널 정수 배율(1·2) = 720·1080·1440·2160. `UiGrade.For(폭, 높이)` = 기준 크기가 다 들어가는 가장 큰 등급 |
| 한글 줄바꿈 `KoText.JoinWords` | Core | 어절 안 글자 사이에 U+2060을 넣어 띄어쓰기에서만 줄이 바뀌게 합니다. 리치 텍스트 태그는 건너뜁니다 |
| 툴팁 자리 `TipPlace.Place` | Core | 대상의 고른 쪽에 붙이고, 화면 밖으로 나가면 반대편에 붙입니다. 자리는 정수 px |
| 트윈 `Tween`·`ISeekable`·`TweenRunner` | Core | 부품은 `Seek(ms)` 하나(같은 ms면 같은 모습). 실행기는 화면에 하나: `Play`(길이가 정해진 부품)·`Drive`(끝을 스스로 아는 것)·`Ambient`(늘 도는 장식), 시계는 `Advance(dtMs)`, `Busy` = 끝이 있는 등록 수 |
| Linear 알파 보정 `LinearAlpha` | Core | `Dim(a)` = `1 − (1 − a)^2.2`(어둡게 덮기), `Glow(a)` = `a^2.2`(어두운 바탕 위 흰 빛) |
| 토큰 USS 두 벌 | `Runtime/UiKit/Resources/RekkonUiKit/Kit720.uss`·`Kit1080.uss` | **생성물** — 원본 `Styles~/Kit.uss`, 아래 「토큰」 |
| `Kit` | UiKit | `Attach`(뿌리에 토큰 벌 · 글꼴 걸기), `Text`·`Wrapped`, `Panel`(창틀 판), `Button`(보통 · 주 행동 · 턴 종료), `Pill`(아이콘 + 숫자), `Badge` |
| `KitSlider`·`SliderMath` | UiKit·Core | 정수 단계 슬라이더. `Changed(값)`는 값이 바뀔 때만 불립니다 |
| `KitTooltip` | UiKit | 툴팁 하나. `Hook`·`Show`·`Hide`. `Hook(…, reading: true)`면 본문 크기 글, `Hook(…, clear: 판)`이면 그 판의 변 밖에 붙습니다 |
| `KitSkin`·`KitFrame`·`KitFont.CreatePixel` | UiKit | 프로젝트가 넣는 글꼴(본문 `Font` · 1080 벌 제목 `TitleFont`)과 9-slice 창틀. 도트 TTF → 동적 FontAsset(`RASTER_HINTED`, Point 아틀라스) |

쓰는 법:

```csharp
var skin = new KitSkin { Font = KitFont.CreatePixel(ttf, 24, 8), Panel = new KitFrame(panelPng, 8), Tip = new KitFrame(tipPng, 8) };
var grade = UiGrade.For(Screen.width, Screen.height);   // panelSettings.scale = grade.PanelScale 은 프로젝트가 넣습니다
Kit.Attach(root, skin, grade);
var tips = new KitTooltip(root, skin);
var end = Kit.Button("턴 종료", KitButtonKind.End);
tips.Hook(end, "턴 종료", "다음 플레이어에게 턴을 넘깁니다.");
```

- 글꼴과 그림 경로는 USS에 없습니다. 코드가 `KitSkin`에서 받아 요소에 겁니다.
- 한 뿌리에는 토큰 벌 하나만 걸립니다(`Attach`를 다시 부르면 바뀝니다).

## 구조

```
package.json
Runtime/Core/    Rekkon.UiKit.Core        순수 C#(noEngineReferences)
Runtime/UiKit/   Rekkon.UiKit             엔진 쪽(UI Toolkit). Core를 참조. Resources/RekkonUiKit/ = 생성된 토큰 USS
Tests/Core/      Rekkon.UiKit.Core.Tests  Core 시험(Unity EditMode와 dotnet test 양쪽에서 돎)
Styles~/Kit.uss  토큰 원본(Unity는 ~로 끝나는 폴더를 들여오지 않음)
Tools~/build-uss.mjs  토큰 생성기(Node)
```

- C# 9입니다. `record`·`init`은 Unity에서 컴파일되지 않습니다.

## 토큰

`Styles~/Kit.uss`가 원본이고 `Kit720.uss`·`Kit1080.uss`는 생성물입니다. 생성물은 손으로 고치지 않습니다.

```bash
node Tools~/build-uss.mjs            # 원본 → 두 벌(바뀐 파일만 씀)
node Tools~/build-uss.mjs --check    # 생성물이 원본과 맞는지 확인
node Tools~/build-uss.mjs --self-test   # 생성기 자기 시험
node Tools~/build-uss.mjs <원본.uss> <출력 폴더>   # 프로젝트의 화면 USS도 같은 문법으로 두 벌을 만듦
```

- 길이는 720 화면 기준 px로 적습니다. 1080 벌은 ×1.5 반올림이고 0px·1px은 그대로입니다. `Npx!`는 두 벌 모두 그 값, `T(a, b)`는 720 벌에 a · 1080 벌에 b입니다.
- 3단입니다: `@palette`(원시 색) → 의미 토큰(`--kit-*: $이름`) → 부품 클래스(`var(--kit-*)`). 색 값을 `@palette` 밖에 적으면 생성이 실패합니다.
- `scrim(r, g, b, a)`는 어둡게 덮는 반투명 색입니다. a는 sRGB 화면 기준 값이고, 생성기가 Linear 색 공간용 `1 − (1 − a)^2.2`로 바꿔 적습니다.
- 글자 크기는 12px · 24px, 1080 벌 제목 36px입니다(`kit-f1` 늘 12 · `kit-f2` 본문 12/24 · `kit-f3` 늘 24 · `kit-ftitle` 창 · 판 제목 24/36). 여러 줄 글은 `Kit.Wrapped`(`kit-wrap`)를 씁니다.
- 줄 세우기 · 간격은 `kit-row`·`kit-gap2/4/6`·`kit-mt2/4/6`을 씁니다.

## 알아 둘 것

- 패널 뿌리의 크기는 테마가 정합니다: `PanelSettings.themeStyleSheet`가 비면 뿌리 요소 높이가 0이 됩니다. 실행 중 만든 `PanelSettings`는 테마가 null이므로, 화면 뿌리를 만드는 쪽이 테마를 넣거나 뿌리를 직접 늘립니다(`StretchToParentSize`).
- `KitFont.CreatePixel`로 만든 글꼴은 비트맵(`RASTER_HINTED`)이라 USS `text-shadow`가 그려지지 않습니다. 그림자가 필요하면 따로 그립니다.
- `KitFont.CreatePixel`로 만든 글꼴을 글자를 막 더한 직후에 없애면 TextCore의 아틀라스 대기열에 남아 다른 패널의 글자 만들기가 `MissingReferenceException`을 낼 수 있습니다(6000.6.3f1). 없애기 전에 대기열을 비웁니다.
- 꺼진 단추는 `opacity: 0.45`입니다.

## 라이선스

이 저장소 뿌리의 `LICENSE.md` 1절을 따릅니다.
