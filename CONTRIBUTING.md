# Contributing to UnoDock

Thank you for helping improve UnoDock.

UnoDock is a desktop-first port of AvalonDock to Uno Platform. The project keeps
AvalonDock-style namespaces and layout model names where practical, while
adapting the control implementation, themes, floating windows, and platform
integration for Uno Skia Desktop.

## Repository Layout

- `src/UnoDock` - core docking manager, layout model, controls, converters, and base theme support.
- `src/UnoDock.Themes.VS2013` - Visual Studio 2013 theme package.
- `src/UnoDock.Themes.VS2010` - Visual Studio 2010 theme package.
- `src/UnoDock.Sample` - desktop sample app and diagnostics surface.
- `src/UnoDock.Tests` - model, layout, and docking behavior tests.
- `externals/AvalonDock` - AvalonDock submodule used as the upstream source reference.
- `docs` - development notes and porting/design records.
- `tools/parity` - helper scripts for visual and behavior parity work.
- `src/UnoDock.slnx` - solution file for local builds.

## Branches

- `master` is shaped for the public repository and may contain a single public
  release commit.

For public contributions, target `master` unless the maintainer asks otherwise.

## License

UnoDock is licensed under the Microsoft Public License (Ms-PL), matching
AvalonDock. By contributing, you agree that your contribution is provided under
the same Ms-PL license.

Keep existing copyright, patent, trademark, license, and attribution notices in
files copied, linked, or adapted from AvalonDock. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
for details.

## Build

Restore submodules first:

```shell
git submodule update --init --recursive
```

Build the solution:

```shell
dotnet build src/UnoDock.slnx
```

Run tests:

```shell
dotnet test src/UnoDock.Tests/UnoDock.Tests.csproj
```

Create local packages:

```powershell
pwsh ./pack.ps1
```

On Windows, the repository also includes `build.windows.bat`, `dist.all.bat`,
and `dist.publish2nugetdotorg.bat` for maintainer packaging workflows.

## Development Guidelines

- Preserve AvalonDock API shape where possible. Public names such as
  `AvalonDock.DockingManager`, `AvalonDock.Layout.LayoutRoot`, and
  `LayoutDocumentPane` are intentional compatibility choices.
- Prefer small, focused changes. Avoid broad rewrites unless they are needed for
  platform parity or maintainability.
- When changing files adapted from AvalonDock, keep upstream comments and
  attribution intact.
- Keep Uno-specific behavior isolated in local forked files, partial classes, or
  platform helpers when possible.
- Update README, package metadata, and third-party notices when a change affects
  package consumers.
- Add or update tests for layout model behavior, serialization, docking
  mutations, and regressions.

## Submodule Notes

`externals/AvalonDock` is a Git submodule. Edits inside that folder are not
captured by the parent repository unless they are committed in the submodule and
the parent repository records the new submodule pointer.

If you need to change the AvalonDock reference:

```shell
cd externals/AvalonDock
git status
git commit -am "Describe upstream-reference change"
cd ../..
git add externals/AvalonDock
git commit -m "Update AvalonDock submodule"
```

For changes that only belong to UnoDock, prefer editing files under `src/UnoDock`
or the theme/sample projects instead of modifying the submodule.

## Packaging Notes

The NuGet packages are:

- `LeXtudio.UnoDock`
- `LeXtudio.UnoDock.Themes.VS2013`
- `LeXtudio.UnoDock.Themes.VS2010`

Each package should include:

- `README.md`
- `LICENSE`
- `THIRD_PARTY_NOTICES.md`
- AvalonDock's Ms-PL license text under `licenses/avalondock_LICENSE.txt`

If you add a new package, mirror that packaging behavior.

## Current Priorities

- More themes
- Logical tree improvements
- Accessibility support
- Floating window polish across Windows and macOS
- WinUI 3 and Linux support planning

## Reporting Issues

When reporting an issue, include:

- Operating system and version
- Uno Platform version
- Package version or commit hash
- Minimal layout snippet or sample project
- Screenshots or screen recordings for visual/docking behavior bugs

For platform support or accelerated feature work, business sponsorship is the
best path. Please reach out through [lextudio.com](https://lextudio.com).
