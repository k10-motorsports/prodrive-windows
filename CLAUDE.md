# prodrive-windows

Single Windows app — WinUI 3 host at [`src/RaceCorProDrive/`](src/RaceCorProDrive/), with the combined Inno Setup installer in [`installer/`](installer/).

Canonical agent instructions live under [`agents/prodrive-windows/`](agents/prodrive-windows/) (pulled in via the [prodrive-agents](https://github.com/k10-motorsports/prodrive-agents) submodule).

Common entry points:
- Repo overview: [`agents/prodrive-windows/CLAUDE.md`](agents/prodrive-windows/CLAUDE.md)
- Cross-repo context: [`agents/prodrive-context/`](agents/prodrive-context/)

To pull updates:

```bash
git submodule update --init --remote agents
```
