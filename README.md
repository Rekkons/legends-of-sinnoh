[한국어](README.ko.md)

# Pokémon: Legends of Sinnoh — Digital Edition

An **unofficial, non-profit fan game** that brings Levatius's Tabletop Simulator board game "Pokémon: Legends of Sinnoh" (v1.4a) to PC. It is not affiliated with the official Pokémon brand.

Made by Rekkons.

## Download

A download page is coming soon. Download links and file checksums will be posted on the [Releases page](https://github.com/Rekkons/legends-of-sinnoh/releases).

## Bugs and questions

Please use [GitHub Issues](https://github.com/Rekkons/legends-of-sinnoh/issues).

## Building

This repository contains only the code. The original Pokémon graphics and sound are not included, so if you open it as is, the screens and sound are empty and the game cannot be played. To play, get the game from the download link above.

- Open this folder with Unity 6000.6.3f1.
- Online rooms need your own Unity Gaming Services project.
- Rule tests: `dotnet test Tools/dotnet-test/SinnohCore.Tests.csproj`
- Unity deletes `.meta` files whose asset is missing, so after opening the project, git may show deleted `.meta` files under `Assets/Sinnoh/Gen/`.

## License

- [LICENSE.md](LICENSE.md) · [CREDITS.md](CREDITS.md) · [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) · [PRIVACY.md](PRIVACY.md)
- The rights to the original Pokémon assets belong to Nintendo, Creatures Inc. and GAME FREAK inc.
