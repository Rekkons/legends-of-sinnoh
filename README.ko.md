[English](README.md)

# Pokémon: Legends of Sinnoh — 디지털판

Levatius의 Tabletop Simulator 보드게임 「Pokémon: Legends of Sinnoh」(v1.4a)를 PC에서 할 수 있게 옮긴 **비공식 · 비영리 팬 게임**입니다. 포켓몬 공식과는 관계없습니다.

만든 사람: Rekkons

## 받기

받기 페이지는 곧 열립니다. 받기 링크와 파일 해시는 [Releases 페이지](https://github.com/Rekkons/legends-of-sinnoh/releases)에 올릴 예정입니다.

## 버그 · 문의

[GitHub Issues](https://github.com/Rekkons/legends-of-sinnoh/issues)에 남겨 주십시오.

## 빌드

이 저장소에는 코드만 있고, 포켓몬 원작 그림과 소리는 들어 있지 않습니다. 그대로 열면 화면과 소리가 비어 있어 플레이할 수 없습니다. 게임은 위 받기 링크에서 받으십시오.

- Unity 6000.6.3f1로 이 폴더를 엽니다.
- 온라인 방은 자기 Unity Gaming Services 프로젝트가 필요합니다.
- 규칙 시험: `dotnet test Tools/dotnet-test/SinnohCore.Tests.csproj`
- Unity는 짝이 되는 파일이 없는 `.meta`를 지우므로, 열고 나면 git에 `Assets/Sinnoh/Gen/`의 `.meta` 삭제가 보일 수 있습니다.

## 라이선스

- [LICENSE.ko.md](LICENSE.ko.md) · [CREDITS.ko.md](CREDITS.ko.md) · [THIRD-PARTY-NOTICES.ko.md](THIRD-PARTY-NOTICES.ko.md) · [PRIVACY.ko.md](PRIVACY.ko.md)
- 포켓몬 원작 자산의 권리는 Nintendo · Creatures Inc. · GAME FREAK inc.에 있습니다.
