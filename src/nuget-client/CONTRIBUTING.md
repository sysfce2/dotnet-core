# Contributing

This project welcomes contributions and suggestions.
Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution.
For details, visit https://cla.dotnetfoundation.org/.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., label, comment).
Simply follow the instructions provided by the bot.
You will only need to do this once across all repositories using our CLA.

This project has adopted the code of conduct defined by the Contributor Covenant
to clarify expected behavior in our community.
For more information, see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## Prerequisites

- [Visual Studio 2022 17.4 or above](https://www.visualstudio.com) with the workloads and components specified in [vsconfig](.vsconfig).
- Windows Powershell v3.0+

## Contributing step-by-step

1. [Open an issue here](https://github.com/NuGet/Home/issues) and get some feedback from the NuGet team.
1. Follow the instructions under [the Code heading](#code).
1. Create a new branch in your local repo and make your changes.
1. Add tests.
    - [Testing in .NET](https://docs.microsoft.com/en-us/dotnet/core/testing/)
    - [Testing tools in Visual Studio](https://docs.microsoft.com/visualstudio/test/)
1. Create a [pull request](https://github.com/NuGet/NuGet.Client/pulls).
    - Create a new issue if you cannot find an existing one [NuGet/Home](https://github.com/NuGet/Home/issues).
    - Keep the pull request template, and link to an issue.
    - Use a meaningful PR title, not the auto-generated title based on the branch name.
    - All PRs created by someone outside the NuGet team will be assigned the `Community` label, and a team member will be assigned as the PR shepherd, who will be responsible for making sure the PR gets reviewed (even if they don't review it themselves), and periodically check the PR for progress.
      - PRs from forks do not trigger CI automatically. Someone in the team needs to apply the "Approved for CI", which will build only the current commit. If changes are pushed to the branch, the "Approved for CI" label needs to be removed and re-applied.
    - If the NuGet team requests changes and the PR author does not respond within 1 month, a reminder will be added. If no action is taken within 2 months of the reminder, the PR will be closed due to inactivity.
1. _One-time_: Sign the contributor license agreement, if you haven't signed it before. The [.NET Foundation Bot](https://github.com/dnfclas) will comment on the pull request you just created and guide you on how to sign the CLA.
1. Submit a doc pull request to the [docs.microsoft-com.nuget](https://github.com/NuGet/docs.microsoft.com-nuget/) repo, if this is a new feature or behavior change.

## Code

The way non-NuGet members contribute to this repository is via the [fork model](https://help.github.com/articles/fork-a-repo/). Contributors push changes to their own "forked" version of NuGet.Client, and then submit a pull request into it requesting those changes be merged.

To get started:

1. Fork the repo.

2. From a git enabled terminal, run (replacing _[user-name]_ with your GitHub user name):

```console
\> git clone https://github.com/[user-name]/NuGet.Client
\> cd NuGet.Client
\NuGet.Client> git remote add upstream https://github.com/NuGet/NuGet.Client
\NuGet.Client> git remote set-url --push upstream no_push
```

After running above, `git remote -v` should show something similar to the following:

```console
\NuGet.Client> git remote -v

origin  https://github.com/[user-name]/NuGet.Client (fetch)
origin  https://github.com/[user-name]/NuGet.Client (push)
upstream        https://github.com/NuGet/NuGet.Client (fetch)
upstream        no_push (push)
```

### NuGet team members

NuGet members may contribute directly to the main remote.

## Build

1. Clone the NuGet.Client repository.

1. Start PowerShell. CD into the cloned repository directory.

1. Run the configuration script

    `.\configure.ps1`

1. Build with

    `.\build.ps1`

   Or Build and Unit test with

   `.\build.ps1 -RunUnitTests`

    > Note: You have to to run .\configure.ps1 and .\build.ps1 at least once in order for your build to succeed.

    > In case you have build issues try cleaning the local repository using `git clean -xdf` and retry steps 3 and 4.

1. Run unit and functional tests with

    `.\runTests.ps1`

1. Run dotnet code formatters and correct any errors.
    - You can use `Format Document` in VS:

       `Ctrl+K, Ctrl+D` or Edit > Advanced > Format Document (https://learn.microsoft.com/visualstudio/ide/default-keyboard-shortcuts-in-visual-studio#bkmk_text-editor-context-specific-shortcuts)

    - You can use the dotnet CLI tool (https://learn.microsoft.com/dotnet/core/tools/dotnet-format):

      `dotnet format whitespace --verify-no-changes NuGet.sln`

### Notable `build.ps1` switches

- `-RunUnitTests` - Runs unit tests after building.
- `-Clean` - cleans build artifacts without deleting files that configure.ps1 creates.

> Reveal all script parameters and switches by running
  Get-Help .\build.ps1 -detailed

### Build artifacts location

- `$(NuGetClientRoot)\artifacts\VS15` - this folder will contain the Package Manager extension (`NuGet.Tools.vsix`) and NuGet command-line client application (`NuGet.exe`)
- `$(NuGetClientRoot)\artifacts\nupkgs` - this folder will contain all our projects packages

## Resources for NuGet.Client development

- [Workflow](docs/workflow.md)
- [Coding Guidelines](docs/coding-guidelines.md)
- [Localizability](docs/localizability.md)
- [UI Guidelines](docs/ui-guidelines.md)
- [Project Overview](docs/project-overview.md)
- [Debugging](docs/debugging.md)
- [New Feature Guide](docs/feature-guide.md)
- [Design Review guide](docs/design-review-guide.md)
- [NuGet Client SDK](docs/nuget-sdk.md)

## Docs generation

To update the auto-generated documentation, run the following in the repo root:

 ```console
 dotnet msbuild .\build\docs.proj
 ```

Updated docs will be at `$(NuGetClientRoot)\docs`
